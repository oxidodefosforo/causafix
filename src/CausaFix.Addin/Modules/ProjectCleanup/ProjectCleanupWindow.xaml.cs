using System;
using System.ComponentModel;
using System.Windows;
using CausaFix.ProjectCleanup.ViewModels;

namespace CausaFix.Addin.Modules.ProjectCleanup
{
    public partial class ProjectCleanupWindow : Window
    {
        public ProjectCleanupWindow()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (e.OldValue is ProjectCleanupViewModel o) o.CloseRequested -= OnCloseRequested;
                if (e.NewValue is ProjectCleanupViewModel n) n.CloseRequested += OnCloseRequested;
            };
            // An exception of this tool on Revit's UI thread must not close Revit.
            Dispatcher.UnhandledException += OnDispatcherException;
            Closed += (s, e) => Dispatcher.UnhandledException -= OnDispatcherException;
        }

        public bool ForceClose { get; set; }
        private ProjectCleanupViewModel Vm => DataContext as ProjectCleanupViewModel;

        private void OnCloseRequested(object sender, EventArgs e) => Close();

        private void OnDispatcherException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            string trace = e.Exception?.ToString() ?? string.Empty;
            if (trace.IndexOf("CausaFix", StringComparison.Ordinal) < 0) return;
            e.Handled = true;
            CausaFix.Addin.Shared.Services.Log.Error("Project Cleanup: unhandled UI exception (Revit kept open)", e.Exception);
            try { MessageBox.Show(this, "Project Cleanup se detuvo por un error (Revit sigue abierto):\n\n" + e.Exception.Message, "CausaFixIt Project Cleanup", MessageBoxButton.OK, MessageBoxImage.Warning); }
            catch (Exception) { }
        }

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
                MessageBox.Show(this, "Project Cleanup está trabajando. Espera a que termine.", "CausaFixIt Project Cleanup");
                e.Cancel = true;
            }
        }
    }
}
