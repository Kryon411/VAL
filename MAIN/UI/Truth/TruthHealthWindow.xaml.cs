using System.Windows;

namespace VAL.UI.Truth
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
