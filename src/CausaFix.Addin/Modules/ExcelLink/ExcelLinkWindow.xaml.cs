using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CausaFix.Addin.Shared;
using CausaFix.ExcelLink;

namespace CausaFix.Addin.Modules.ExcelLink
{
    public partial class ExcelLinkWindow : Window
    {
        private ExcelLinkViewModel _vm;

        public ExcelLinkWindow()
        {
            InitializeComponent();
        }

        /// <summary>Set when the model is closing: the window closes without asking about pending edits.</summary>
        public bool ForceClose { get; set; }

        public void Attach(ExcelLinkViewModel vm, IClipboard clipboard)
        {
            _vm = vm;
            DataContext = vm;
            PreviewPane.Attach(vm, clipboard);
        }

        public async void Initialize()
        {
            try { await _vm.InitializeAsync(); }
            catch (Exception ex) { _vm.ReportError(ex); }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (ForceClose || _vm == null) return;
            if (_vm.IsBusy)
            {
                MessageBox.Show(this, "Excel Link is working. Cancel or wait before closing.", "CausaFixIt Excel Link");
                e.Cancel = true;
                return;
            }
            int pending = _vm.Preview.PendingCount + _vm.Preview.InvalidCount;
            if (pending > 0 && MessageBox.Show(this, pending + " pending edit(s) in Preview/Edit have not been applied and will be discarded. Close anyway?",
                    "CausaFixIt Excel Link", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                e.Cancel = true;
        }

        private static void OpenMenu(object sender)
        {
            if (sender is Button b && b.ContextMenu != null)
            {
                b.ContextMenu.DataContext = b.DataContext;
                b.ContextMenu.PlacementTarget = b;
                b.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                b.ContextMenu.IsOpen = true;
            }
        }

        private void OnProfileMenu(object sender, RoutedEventArgs e) => OpenMenu(sender);
        private void OnToolsMenu(object sender, RoutedEventArgs e) => OpenMenu(sender);
        private void OnMoreMenu(object sender, RoutedEventArgs e) => OpenMenu(sender);
    }
}
