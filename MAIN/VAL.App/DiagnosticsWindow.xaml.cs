using System.Windows;
using VAL.ViewModels;

namespace VAL
{
    public partial class DiagnosticsWindow : Window
    {
        public DiagnosticsWindow(DiagnosticsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
