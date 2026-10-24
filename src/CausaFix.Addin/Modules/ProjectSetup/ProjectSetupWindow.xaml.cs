using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CausaFix.ProjectSetup.ViewModels;

namespace CausaFix.Addin.Modules.ProjectSetup
{
    public partial class ProjectSetupWindow : Window
    {
        public ProjectSetupWindow()
        {
            InitializeComponent();
            // Safety net: an exception raised by this tool on Revit's UI thread must not close Revit.
            Dispatcher.UnhandledException += OnDispatcherException;
            Closed += (s, e) => Dispatcher.UnhandledException -= OnDispatcherException;
        }

        private void OnDispatcherException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            string trace = e.Exception?.ToString() ?? string.Empty;
            if (trace.IndexOf("CausaFix", StringComparison.Ordinal) < 0) return; // not ours: leave it to Revit
            e.Handled = true;
            CausaFix.Addin.Shared.Services.Log.Error("Project Setup: unhandled UI exception (Revit kept open)", e.Exception);
            try
            {
                MessageBox.Show(this, "Project Setup hit an error and stopped this action (Revit was kept open):\n\n" + e.Exception.Message +
                                      "\n\nDetails are in the CausaFix log.", "CausaFix Project Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception) { }
        }

        public bool ForceClose { get; set; }

        private ProjectSetupViewModel Vm => DataContext as ProjectSetupViewModel;

        public async void Initialize()
        {
            try { await Vm.InitializeAsync(); }
            catch (Exception ex) { Vm.ReportError(ex); }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (ForceClose || Vm == null) return;
            if (Vm.IsBusy)
            {
                MessageBox.Show(this, "Project Setup is working. Wait for it to finish.", "CausaFix Project Setup");
                e.Cancel = true;
            }
        }

        /// <summary>
        /// Keyboard focus moved to a control inside a rule row (normally the Enabled box): select that row. Deferred to
        /// input priority so the previous editor's LostFocus bindings commit to the rule that was being edited before the
        /// selection (and the editor's DataContext) changes. Clicks on the text are plain ListBoxItem selection.
        /// </summary>
        private void OnRuleItemFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if (!(sender is ListBoxItem item) || item.IsSelected || ReferenceEquals(e.NewFocus, item)) return;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() => { if (!item.IsSelected) item.IsSelected = true; }));
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
    }
}
