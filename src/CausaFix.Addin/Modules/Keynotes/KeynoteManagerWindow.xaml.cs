using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CausaFix.Keynotes.Execution;
using CausaFix.Keynotes.ViewModels;

namespace CausaFix.Addin.Modules.Keynotes
{
    public partial class KeynoteManagerWindow : Window
    {
        private Point _dragStart;
        private KeynoteRow _dragRow;
        private bool _closingChecked;

        public KeynoteManagerWindow()
        {
            InitializeComponent();
            UsageCombo.ItemsSource = Enum.GetValues(typeof(UsageFilter));
            SourceCombo.ItemsSource = Enum.GetValues(typeof(SourceFilter));
            DataContextChanged += (s, e) =>
            {
                if (e.OldValue is KeynoteManagerViewModel o) o.CloseRequested -= OnCloseRequested;
                if (e.NewValue is KeynoteManagerViewModel n) n.CloseRequested += OnCloseRequested;
            };
            PreviewKeyDown += OnPreviewKeyDown;
            // An exception of this tool on Revit's UI thread must not close Revit.
            Dispatcher.UnhandledException += OnDispatcherException;
            Closed += (s, e) => Dispatcher.UnhandledException -= OnDispatcherException;
        }

        public bool ForceClose { get; set; }
        public KeynoteManagerViewModel Vm => DataContext as KeynoteManagerViewModel;

        private void OnCloseRequested(object sender, EventArgs e) { _closingChecked = true; Close(); }

        private void OnDispatcherException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            string trace = e.Exception?.ToString() ?? string.Empty;
            if (trace.IndexOf("CausaFix", StringComparison.Ordinal) < 0) return;
            e.Handled = true;
            Shared.Services.Log.Error("Keynote Manager: unhandled UI exception (Revit kept open)", e.Exception);
            try { MessageBox.Show(this, "Keynote Manager encontró un error (Revit sigue abierto; tus cambios siguen en el borrador):\n\n" + e.Exception.Message, "CausaFixIt Keynote Manager", MessageBoxButton.OK, MessageBoxImage.Warning); }
            catch (Exception) { }
        }

        public async void Initialize()
        {
            try { await Vm.InitializeAsync(); }
            catch (Exception ex) { Shared.Services.Log.Error("Keynote Manager: initialize", ex); MessageBox.Show(this, ex.Message, "CausaFixIt Keynote Manager"); }
        }

        protected override async void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (ForceClose || _closingChecked || Vm == null) return;
            e.Cancel = true;
            if (Vm.IsBusy) { MessageBox.Show(this, "Keynote Manager está trabajando. Espera a que termine.", "CausaFixIt Keynote Manager"); return; }
            bool ok;
            try { ok = await Vm.ConfirmCloseAsync(); }
            catch (Exception ex) { Shared.Services.Log.Error("Keynote Manager: close", ex); ok = true; }
            if (!ok) return;
            _closingChecked = true;
            Close();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        // ------------------------------------------------------------------ rows

        /// <summary>Focus that lands inside a row (expander button) selects that row too.</summary>
        private void OnRowFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!(sender is ListBoxItem item) || item.IsSelected || ReferenceEquals(e.NewFocus, item)) return;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() => { if (!item.IsSelected) item.IsSelected = true; }));
        }

        private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject d && FindParent<Button>(d) != null) return;
            if ((sender as ListBoxItem)?.DataContext is KeynoteRow row)
            {
                row.IsSelected = true;
                if (Vm.EditCommand.CanExecute(null)) Vm.EditCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void OnToggle(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is KeynoteRow row)
            {
                row.IsSelected = true;
                row.Toggle();
            }
        }

        private void OnViewDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ViewsList.SelectedItem is ViewRef v && Vm.ShowTagsCommand.CanExecute(v)) Vm.ShowTagsCommand.Execute(v);
        }

        private void OnMenu(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.ContextMenu != null)
            {
                b.ContextMenu.DataContext = b.DataContext;
                b.ContextMenu.PlacementTarget = b;
                b.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                b.ContextMenu.IsOpen = true;
            }
        }

        // ------------------------------------------------------------------ keyboard

        private static bool Run(System.Windows.Input.ICommand c, object p = null)
        {
            if (c != null && c.CanExecute(p)) { c.Execute(p); return true; }
            return true;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var vm = Vm;
            if (vm == null) return;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            // Typing in a text box keeps its own keys (Ctrl+C/V, Delete, Tab…), except the window-wide ones below.
            bool typing = Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox;
            if (ctrl && e.Key == Key.S) { e.Handled = Run(vm.SaveReloadCommand); return; }
            if (e.Key == Key.F5) { e.Handled = Run(vm.RefreshCommand); return; }
            if (ctrl && e.Key == Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; return; }
            if (typing) return;
            if (e.Key == Key.F2) e.Handled = Run(vm.EditCommand);
            else if (ctrl && e.Key == Key.N) e.Handled = Run(vm.NewKeynoteCommand);
            else if (ctrl && e.Key == Key.D) e.Handled = Run(vm.DuplicateCommand);
            else if (ctrl && e.Key == Key.C) e.Handled = Run(vm.CopyCommand);
            else if (ctrl && e.Key == Key.V) e.Handled = Run(vm.PasteCommand);
            else if (e.Key == Key.Delete) e.Handled = Run(vm.DeleteCommand);
            else if (e.Key == Key.Tab && vm.SelectedRow != null && Keyboard.FocusedElement is ListBoxItem) e.Handled = Run(shift ? vm.OutdentCommand : vm.IndentCommand);
            else if (e.Key == Key.Enter && Keyboard.FocusedElement is ListBoxItem) e.Handled = Run(vm.EditCommand);
            else if ((e.Key == Key.Right || e.Key == Key.Left) && vm.SelectedRow != null && vm.SelectedRow.HasChildren && Keyboard.FocusedElement is ListBoxItem)
            {
                if ((e.Key == Key.Right) != vm.SelectedRow.IsExpanded) vm.SelectedRow.Toggle();
                e.Handled = true;
            }
            if (e.Handled) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(FocusSelected));
        }

        private void FocusSelected()
        {
            var row = Vm?.SelectedRow;
            if (row == null) return;
            Tree.ScrollIntoView(row);
            if (Tree.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem item) item.Focus();
        }

        // ------------------------------------------------------------------ drag and drop (reparent; keys never change)

        private void OnTreeMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(Tree);
            _dragRow = (e.OriginalSource is DependencyObject d && FindParent<Button>(d) == null) ? FindParent<ListBoxItem>(d)?.DataContext as KeynoteRow : null;
        }

        private void OnTreeMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragRow == null || e.LeftButton != MouseButtonState.Pressed || Vm == null || !Vm.CanEdit) return;
            Vector diff = e.GetPosition(Tree) - _dragStart;
            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var row = _dragRow;
            _dragRow = null;
            DragDrop.DoDragDrop(Tree, new DataObject("CausaFixKeynote", row.Key), DragDropEffects.Move);
        }

        private void OnTreeDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent("CausaFixKeynote") ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnTreeDrop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData("CausaFixKeynote") is string key) || Vm == null) return;
            var target = (e.OriginalSource as DependencyObject) == null ? null : FindParent<ListBoxItem>((DependencyObject)e.OriginalSource)?.DataContext as KeynoteRow;
            if (target != null && target.Key == key) return;
            Vm.Move(key, target?.Key ?? "");
            e.Handled = true;
        }

        private static T FindParent<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T)) d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }
    }
}
