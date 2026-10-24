using System.Windows.Controls;
using System.Windows.Input;
using CausaFix.ProjectSetup.ViewModels;

namespace CausaFix.Addin.Modules.ProjectSetup
{
    /// <summary>Build Name editor (DataContext: <see cref="NameRuleItem"/>).</summary>
    public partial class NameBuilderControl : UserControl
    {
        public NameBuilderControl()
        {
            InitializeComponent();
        }

        private void OnPickerDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is NameRuleItem item && item.AddCommand.CanExecute(null)) item.AddCommand.Execute(null);
        }
    }
}
