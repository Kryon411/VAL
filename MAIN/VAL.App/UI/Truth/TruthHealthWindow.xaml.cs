using System.Windows;
using VAL.Host.Services;

namespace VAL.UI.Truth
{
    public partial class TruthHealthWindow : Window
    {
        public TruthHealthWindow(ITruthHealthReportService reportService)
        {
            InitializeComponent();
            DataContext = new TruthHealthViewModel(reportService);
        }
    }
}
