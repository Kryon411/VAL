using System;
using System;
using System.Windows;

namespace VAL.App
{
    public partial class CrashWindow : Window
    {
        public CrashWindow(CrashWindowViewModel viewModel, CrashWindowRequest request)
        {
            ArgumentNullException.ThrowIfNull(viewModel);
            ArgumentNullException.ThrowIfNull(request);

            viewModel.Initialize(request);
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
