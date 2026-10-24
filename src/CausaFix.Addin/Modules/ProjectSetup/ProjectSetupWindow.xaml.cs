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
