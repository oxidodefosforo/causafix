using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CausaFix.ExcelLink;

namespace CausaFix.Addin.Modules.ExcelLink
{
    public partial class CategoryTabView : UserControl
    {
        public CategoryTabView()
        {
            InitializeComponent();
        }

        private CategoryTabViewModel Vm => DataContext as CategoryTabViewModel;

        private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e) => Vm?.Parameters.NotifySelectionChanged();

        private static ParamItem ItemUnderMouse(object sender, MouseButtonEventArgs e)
        {
            var element = e.OriginalSource as FrameworkElement;
            return element?.DataContext as ParamItem ?? (sender as ListBox)?.SelectedItem as ParamItem;
        }

        private void OnAvailableDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = ItemUnderMouse(sender, e);
            if (item != null) Vm?.Parameters.AddOneCommand.Execute(item);
        }

        private void OnSelectedDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = ItemUnderMouse(sender, e);
            if (item != null) Vm?.Parameters.RemoveOneCommand.Execute(item);
        }
    }
}
