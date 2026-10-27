using System.Windows;
using System.Windows.Controls;
using CausaFix.ExcelLink;

namespace CausaFix.Addin.Modules.ExcelLink
{
    public partial class ReviewWindow : Window
    {
        public ReviewWindow()
        {
            InitializeComponent();
        }

        private ReviewViewModel Vm => DataContext as ReviewViewModel;

        private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Vm == null || FilterBox == null) return;
            Vm.Filter = (ReviewViewModel.EntryFilter)FilterBox.SelectedIndex;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (Vm != null && Vm.IsBusy)
            {
                MessageBox.Show(this, "Changes are being applied. Please wait.", "CausaFixIt");
                e.Cancel = true;
                return;
            }
            base.OnClosing(e);
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
