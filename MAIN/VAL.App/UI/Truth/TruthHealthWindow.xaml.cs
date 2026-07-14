using System.Windows;

namespace VAL.App.UI.Truth
{
    public partial class TruthHealthWindow : Window
    {
        public TruthHealthWindow(TruthHealthViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
