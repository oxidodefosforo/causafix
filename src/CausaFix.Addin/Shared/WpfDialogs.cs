using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CausaFix.Addin.Modules.ExcelLink;
using CausaFix.ExcelLink;

namespace CausaFix.Addin.Shared
{
    /// <summary>WPF implementation of the dialogs the view models request.</summary>
    public sealed class WpfDialogs : IDialogService
    {
        private readonly Window _owner;

        public WpfDialogs(Window owner) { _owner = owner; }

        private bool? Ask(Microsoft.Win32.CommonDialog d) => _owner != null && _owner.IsVisible ? d.ShowDialog(_owner) : d.ShowDialog();

        private static string ValidFolder(string folder) => !string.IsNullOrEmpty(folder) && Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        public string PickSaveWorkbook(string suggestedName, string folder)
        {
            var d = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export to Excel",
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = suggestedName,
                InitialDirectory = ValidFolder(folder)
            };
            return Ask(d) == true ? d.FileName : null;
        }

        public string PickOpenWorkbook(string folder)
        {
            var d = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import from Excel",
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                CheckFileExists = true,
                InitialDirectory = ValidFolder(folder)
            };
            return Ask(d) == true ? d.FileName : null;
        }

        public string PickOpenProfile()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Import profile", Filter = "CausaFix profile (*.json)|*.json", CheckFileExists = true };
            return Ask(d) == true ? d.FileName : null;
        }

        public string PickSaveProfile(string suggestedName)
        {
            var d = new Microsoft.Win32.SaveFileDialog { Title = "Export profile", Filter = "CausaFix profile (*.json)|*.json", FileName = suggestedName, DefaultExt = ".json" };
            return Ask(d) == true ? d.FileName : null;
        }

        public bool Confirm(string title, string message)
            => Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

        public void Info(string title, string message) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

        public void Error(string title, string message) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

        private MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
            => _owner != null && _owner.IsVisible ? MessageBox.Show(_owner, message, title, buttons, image) : MessageBox.Show(message, title, buttons, image);

        public string Prompt(string title, string label, string defaultValue)
        {
            var box = new TextBox { Text = defaultValue ?? string.Empty, Margin = new Thickness(0, 6, 0, 12), MinWidth = 320 };
            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = label });
            panel.Children.Add(box);
            panel.Children.Add(buttons);
            var w = NewDialog(title, panel);
            ok.Click += (s, e) => { w.DialogResult = true; };
            w.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
            return w.ShowDialog() == true ? box.Text : null;
        }

        public int Choose(string title, string message, IList<string> options)
        {
            var panel = new StackPanel { Margin = new Thickness(16), MaxWidth = 520 };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            var radios = new List<RadioButton>();
            foreach (var o in options)
            {
                var rb = new RadioButton { Content = o, Margin = new Thickness(0, 3, 0, 3), GroupName = "choice" };
                radios.Add(rb);
                panel.Children.Add(rb);
            }
            if (radios.Count > 0) radios[0].IsChecked = true;
            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 12, 8, 0) };
            var cancel = new Button { Content = "Skip", IsCancel = true, MinWidth = 80, Margin = new Thickness(0, 12, 0, 0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            var w = NewDialog(title, panel);
            ok.Click += (s, e) => { w.DialogResult = true; };
            if (w.ShowDialog() != true) return -1;
            return radios.FindIndex(r => r.IsChecked == true);
        }

        private Window NewDialog(string title, UIElement content)
        {
            var w = new Window
            {
                Title = title,
                Content = content,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = _owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                UseLayoutRounding = true
            };
            if (_owner != null && _owner.IsVisible) w.Owner = _owner;
            return w;
        }

        public void OpenWithShell(string path) => Services.OpenWithShell(path);

        public void ShowReview(ReviewViewModel review)
        {
            // Modeless on purpose: Apply runs through an ExternalEvent, which Revit processes only from its
            // own message loop (a modal dialog would block it). The main window is disabled meanwhile.
            var w = new ReviewWindow { DataContext = review };
            if (_owner != null)
            {
                w.Owner = _owner;
                _owner.IsEnabled = false;
                w.Closed += (s, e) => { _owner.IsEnabled = true; _owner.Activate(); };
            }
            w.Show();
        }
    }

    public sealed class WpfClipboard : IClipboard
    {
        public string GetText()
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty; }
            catch (Exception) { return string.Empty; }
        }

        public void SetText(string text)
        {
            try { Clipboard.SetText(text ?? string.Empty); }
            catch (Exception) { /* clipboard busy */ }
        }
    }
}
