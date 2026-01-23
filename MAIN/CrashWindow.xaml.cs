using System;
using System.Windows;
using VAL.Host.Services;
using VAL.ViewModels;

namespace VAL
{
    public partial class CrashWindow : Window
    {
        public CrashWindow(string crashDetails, string logsRoot, IProcessLauncher processLauncher)
        {
            InitializeComponent();
            DataContext = new CrashWindowViewModel(crashDetails, logsRoot, processLauncher);
        }

        private sealed class CrashWindowViewModel
        {
            private readonly string _logsRoot;
            private readonly IProcessLauncher _processLauncher;

            public CrashWindowViewModel(string crashDetails, string logsRoot, IProcessLauncher processLauncher)
            {
                CrashDetails = crashDetails ?? string.Empty;
                _logsRoot = logsRoot;
                _processLauncher = processLauncher;

                CopyCrashDetailsCommand = new RelayCommand(CopyCrashDetails);
                OpenLogsFolderCommand = new RelayCommand(OpenLogsFolder);
            }

            public string CrashDetails { get; }
            public RelayCommand CopyCrashDetailsCommand { get; }
            public RelayCommand OpenLogsFolderCommand { get; }

            private void CopyCrashDetails()
            {
                try
                {
                    Clipboard.SetText(CrashDetails);
                }
                catch
                {
                    // Clipboard failures should not crash.
                }
            }

            private void OpenLogsFolder()
            {
                try
                {
                    _processLauncher.OpenFolder(_logsRoot);
                }
                catch
                {
                    // Never throw in crash UI.
                }
            }
        }
    }
}
