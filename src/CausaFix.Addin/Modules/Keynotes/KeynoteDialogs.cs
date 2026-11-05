using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CausaFix.Addin.Shared;
using CausaFix.Keynotes.Editing;
using CausaFix.Keynotes.Execution;
using CausaFix.Keynotes.Model;

namespace CausaFix.Addin.Modules.Keynotes
{
    /// <summary>WPF dialogs of Keynote Manager (record form, reviews, conflicts, files, clipboard).</summary>
    public sealed class KeynoteDialogs : IKeynoteDialogs
    {
        private readonly Window _owner;
        private readonly WpfDialogs _basic;
        private readonly WpfClipboard _clip = new WpfClipboard();

        public KeynoteDialogs(Window owner) { _owner = owner; _basic = new WpfDialogs(owner); }

        private Window NewWindow(string title, UIElement content, double width, double height)
        {
            var w = new Window
            {
                Title = title, Content = content, Width = width, Height = height, MinWidth = 420, MinHeight = 260,
                WindowStartupLocation = _owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false, UseLayoutRounding = true, FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF9, 0xFA))
            };
            if (_owner != null && _owner.IsVisible) w.Owner = _owner;
            return w;
        }

        private static StackPanel Buttons(params Button[] b)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            foreach (var x in b) { x.MinWidth = 90; x.Margin = new Thickness(6, 0, 0, 0); x.Padding = new Thickness(10, 3, 10, 3); p.Children.Add(x); }
            return p;
        }

        private static Button Accent(string text, bool danger = false)
            => new Button { Content = text, IsDefault = true, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Background = new SolidColorBrush(danger ? Color.FromRgb(0xB0, 0x3A, 0x2E) : Color.FromRgb(0x0E, 0x7C, 0x86)) };

        // ------------------------------------------------------------------ record form

        public RecordFormResult EditRecord(RecordFormRequest r)
        {
            var root = new DockPanel { Margin = new Thickness(14) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 6; i++) grid.RowDefinitions.Add(new RowDefinition { Height = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

            void Label(string t, int row) { var l = new TextBlock { Text = t, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0), FontWeight = FontWeights.SemiBold }; Grid.SetRow(l, row); grid.Children.Add(l); }
            T Put<T>(T c, int row) where T : UIElement { Grid.SetRow(c, row); Grid.SetColumn(c, 1); grid.Children.Add(c); return c; }

            Label("Key", 0);
            var key = Put(new TextBox { Text = r.Key ?? "", IsEnabled = r.KeyEditable, Margin = new Thickness(0, 3, 0, 3), FontFamily = new FontFamily("Consolas"), FontSize = 13 }, 0);
            AutomationPropertiesName(key, "Key");
            Label("Parent", 1);
            var parent = Put(new ComboBox { IsEnabled = r.ParentEditable, Margin = new Thickness(0, 3, 0, 3), IsEditable = false }, 1);
            parent.ItemsSource = r.ParentChoices.Select(p => new KeyValuePair<string, string>(p.Key, string.IsNullOrEmpty(p.Key) ? p.Text : p.Key + " – " + p.Text)).ToList();
            parent.DisplayMemberPath = "Value";
            parent.SelectedValuePath = "Key";
            parent.SelectedValue = r.ParentKey ?? "";
            if (parent.SelectedIndex < 0) parent.SelectedIndex = 0;
            Label("Text", 2);
            var text = Put(new TextBox { Text = r.Text ?? "", AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 90, Margin = new Thickness(0, 3, 0, 3) }, 2);
            AutomationPropertiesName(text, "Text");
            var preview = Put(new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x5F, 0xBF)), Margin = new Thickness(0, 0, 0, 4) }, 3);
            var templates = new ComboBox { MinWidth = 160, Margin = new Thickness(0, 0, 0, 4) };
            templates.ItemsSource = new[] { "(insert template…)" }.Concat(r.Templates ?? new List<string>()).ToList();
            templates.SelectedIndex = 0;
            templates.SelectionChanged += (s, e) => { if (templates.SelectedIndex > 0) { text.Text = (string)templates.SelectedItem; templates.SelectedIndex = 0; } };
            Put(templates, 4).SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            var hint = Put(new TextBlock { Text = r.Hint ?? "", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x76, 0x80)), FontSize = 11 }, 5);
            var error = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x00, 0x20)), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
            void UpdatePreview()
            {
                preview.Text = KeynoteText.NeedsNormalization(text.Text) ? "Will be saved as one line: " + KeynoteText.Normalize(text.Text) : "";
            }
            text.TextChanged += (s, e) => { UpdatePreview(); error.Text = ""; };
            key.TextChanged += (s, e) => error.Text = "";
            UpdatePreview();

            var ok = Accent("OK");
            var cancel = new Button { Content = "Cancel", IsCancel = true };
            var buttons = Buttons(ok, cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            DockPanel.SetDock(error, Dock.Bottom);
            root.Children.Add(buttons);
            root.Children.Add(error);
            root.Children.Add(grid);
            var w = NewWindow(r.Title, root, 620, 420);
            RecordFormResult result = null;
            ok.Click += (s, e) =>
            {
                string k = (key.Text ?? "").Trim(), t = KeynoteText.Normalize(text.Text), p = parent.SelectedValue as string ?? "";
                string err = r.Validate?.Invoke(k, t, p);
                if (err != null) { error.Text = err; return; }
                result = new RecordFormResult { Key = k, Text = t, ParentKey = p };
                w.DialogResult = true;
            };
            w.Loaded += (s, e) => { if (r.KeyEditable && string.IsNullOrEmpty(r.Key)) key.Focus(); else { text.Focus(); text.SelectAll(); } };
            return w.ShowDialog() == true ? result : null;
        }

        private static void AutomationPropertiesName(DependencyObject d, string name) => System.Windows.Automation.AutomationProperties.SetName(d, name);

        // ------------------------------------------------------------------ reviews

        public bool Review(string title, string intro, IList<string> lines, string confirmText, bool danger = false)
        {
            var root = new DockPanel { Margin = new Thickness(14) };
            var top = new TextBlock { Text = intro ?? "", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(top, Dock.Top);
            var ok = Accent(confirmText, danger);
            var cancel = new Button { Content = "Cancel", IsCancel = true };
            bool single = confirmText == "OK";
            var buttons = single ? Buttons(ok) : Buttons(ok, cancel);
            if (single) ok.IsCancel = true;
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(top);
            root.Children.Add(buttons);
            var list = new ListBox { ItemsSource = lines ?? new List<string>() };
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            list.ItemTemplate = TextTemplate();
            root.Children.Add(list);
            var w = NewWindow(title, root, 720, Math.Min(620, 220 + 22 * Math.Max(1, lines?.Count ?? 0)));
            ok.Click += (s, e) => w.DialogResult = true;
            bool? answer = w.ShowDialog();
            return single || answer == true;
        }

        private static DataTemplate TextTemplate()
        {
            var f = new FrameworkElementFactory(typeof(TextBlock));
            f.SetBinding(TextBlock.TextProperty, new Binding("."));
            f.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            f.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1));
            return new DataTemplate { VisualTree = f };
        }

        public bool ReviewIncoming(string title, string intro, IList<IncomingItem> items, bool keysEditable)
        {
            var rows = items.Select(i => new IncomingRow(i)).ToList();
            var root = new DockPanel { Margin = new Thickness(14) };
            var top = new TextBlock { Text = intro ?? "", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(top, Dock.Top);
            var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal };
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Take", Binding = new Binding(nameof(IncomingRow.Take)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 50 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding(nameof(IncomingRow.Status)), IsReadOnly = true, Width = 80 });
            grid.Columns.Add(new DataGridTextColumn { Header = keysEditable ? "New key (editable)" : "Key", Binding = new Binding(nameof(IncomingRow.TargetKey)), IsReadOnly = !keysEditable, Width = 140 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Parent", Binding = new Binding(nameof(IncomingRow.Parent)), IsReadOnly = true, Width = 110 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Incoming text", Binding = new Binding(nameof(IncomingRow.Text)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Current / note", Binding = new Binding(nameof(IncomingRow.Current)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            var ok = Accent("Apply selected");
            var all = new Button { Content = "Select new" };
            all.Click += (s, e) => { foreach (var r in rows.Where(x => x.Item.Status == IncomingStatus.New)) r.Take = true; grid.Items.Refresh(); };
            var cancel = new Button { Content = "Cancel", IsCancel = true };
            var buttons = Buttons(all, ok, cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(top);
            root.Children.Add(buttons);
            root.Children.Add(grid);
            var w = NewWindow(title, root, 980, 600);
            ok.Click += (s, e) => { grid.CommitEdit(DataGridEditingUnit.Row, true); w.DialogResult = true; };
            return w.ShowDialog() == true;
        }

        private sealed class IncomingRow
        {
            public IncomingRow(IncomingItem i) { Item = i; }
            public IncomingItem Item { get; }
            public bool Take { get => Item.Take; set => Item.Take = value; }
            public string Status => Item.Status == IncomingStatus.New ? "New" : Item.Status == IncomingStatus.Identical ? "Identical" : "Conflict";
            public string TargetKey { get => Item.TargetKey; set => Item.TargetKey = (value ?? "").Trim(); }
            public string Parent => string.IsNullOrEmpty(Item.TargetParent) ? "(top level)" : Item.TargetParent;
            public string Text => Item.Incoming?.Text ?? "";
            public string Current => (Item.Existing != null && Item.Status == IncomingStatus.Conflict ? "Current: " + Item.Existing.Text + ". " : "") + Item.Note;
        }

        public bool ResolveConflicts(MergeResult merge)
        {
            var rows = merge.Conflicts.Select(c => new ConflictRow(c)).ToList();
            var root = new DockPanel { Margin = new Thickness(14) };
            var top = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
                Text = "The file changed while you were editing, and these keynotes were changed differently on both sides. Choose which version to save for each one (nothing is overwritten without a choice)." };
            DockPanel.SetDock(top, Dock.Top);
            var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column };
            grid.Columns.Add(new DataGridTextColumn { Header = "Key", Binding = new Binding(nameof(ConflictRow.Key)), IsReadOnly = true, Width = 110 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Original", Binding = new Binding(nameof(ConflictRow.Base)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Mine (draft)", Binding = new Binding(nameof(ConflictRow.Mine)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Theirs (current file)", Binding = new Binding(nameof(ConflictRow.Theirs)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridComboBoxColumn { Header = "Save", ItemsSource = new[] { "Mine", "Theirs" }, SelectedItemBinding = new Binding(nameof(ConflictRow.Pick)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 80 });
            var mine = new Button { Content = "All mine" };
            var theirs = new Button { Content = "All theirs" };
            mine.Click += (s, e) => { rows.ForEach(r => r.Pick = "Mine"); grid.Items.Refresh(); };
            theirs.Click += (s, e) => { rows.ForEach(r => r.Pick = "Theirs"); grid.Items.Refresh(); };
            var ok = Accent("Save merged");
            var cancel = new Button { Content = "Cancel (do not save)", IsCancel = true };
            var err = new TextBlock { Foreground = Brushes.Firebrick, FontWeight = FontWeights.SemiBold };
            var buttons = Buttons(mine, theirs, ok, cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            DockPanel.SetDock(err, Dock.Bottom);
            root.Children.Add(top); root.Children.Add(buttons); root.Children.Add(err); root.Children.Add(grid);
            var w = NewWindow("Conflicts on save", root, 1000, 520);
            ok.Click += (s, e) =>
            {
                grid.CommitEdit(DataGridEditingUnit.Row, true);
                if (merge.Conflicts.Any(c => c.Choice == ConflictChoice.Unresolved)) { err.Text = "Choose \"Mine\" or \"Theirs\" on every row."; return; }
                w.DialogResult = true;
            };
            return w.ShowDialog() == true;
        }

        private sealed class ConflictRow
        {
            private readonly MergeConflict _c;
            public ConflictRow(MergeConflict c) { _c = c; }
            public string Key => _c.Key;
            public string Base => MergeConflict.Show(_c.Base);
            public string Mine => MergeConflict.Show(_c.Mine);
            public string Theirs => MergeConflict.Show(_c.Theirs);
            public string Pick
            {
                get => _c.Choice == ConflictChoice.Mine ? "Mine" : _c.Choice == ConflictChoice.Theirs ? "Theirs" : null;
                set => _c.Choice = value == "Mine" ? ConflictChoice.Mine : value == "Theirs" ? ConflictChoice.Theirs : ConflictChoice.Unresolved;
            }
        }

        // ------------------------------------------------------------------ simple

        public CloseChoice AskSaveDiscardCancel(string message)
        {
            var r = MessageBox.Show(_owner, message + "\n\nYes = save (and reload if it is the model's file)\nNo = discard the changes\nCancel = keep editing", "CausaFixIt Keynote Manager", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            return r == MessageBoxResult.Yes ? CloseChoice.Save : r == MessageBoxResult.No ? CloseChoice.Discard : CloseChoice.Cancel;
        }

        public bool Confirm(string title, string message) => _basic.Confirm(title, message);
        public void Info(string title, string message) => _basic.Info(title, message);
        public void Error(string title, string message) => _basic.Error(title, message);
        public string AskText(string title, string prompt, string initial) => _basic.Prompt(title, prompt, initial);

        private static void Initial(Microsoft.Win32.FileDialog d, string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                string dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) d.InitialDirectory = dir;
                if (!Directory.Exists(path)) d.FileName = Path.GetFileName(path);
            }
            catch { }
        }

        public string PickOpenFile(string title, string filter, string initialPath)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
            Initial(d, initialPath);
            d.FileName = "";
            return (_owner != null && _owner.IsVisible ? d.ShowDialog(_owner) : d.ShowDialog()) == true ? d.FileName : null;
        }

        public string PickSaveFile(string title, string filter, string initialPath)
        {
            var d = new Microsoft.Win32.SaveFileDialog { Title = title, Filter = filter, DefaultExt = ".txt", AddExtension = true, OverwritePrompt = false };
            Initial(d, initialPath);
            return (_owner != null && _owner.IsVisible ? d.ShowDialog(_owner) : d.ShowDialog()) == true ? d.FileName : null;
        }

        public void SetClipboard(string text) => _clip.SetText(text);
        public string GetClipboard() => _clip.GetText();

        public void ShowInFolder(string path)
        {
            try
            {
                if (File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
                else if (Directory.Exists(Path.GetDirectoryName(path) ?? "")) Services.OpenWithShell(Path.GetDirectoryName(path));
            }
            catch (Exception ex) { Error("Open folder", ex.Message); }
        }
    }
}
