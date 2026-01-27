using System.Windows;
using VAL.Host.Services;

namespace VAL.UI.Truth
{
    public partial class TruthHealthWindow : Window
    {
        public TruthHealthWindow()
        {
            InitializeComponent();
            DataContext = new TruthHealthViewModel(new TruthHealthReportService());
        }
    }
}
