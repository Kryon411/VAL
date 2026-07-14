using System.Windows;

namespace VAL.App
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
