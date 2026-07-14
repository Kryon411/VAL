using System;
using System.Windows;

using VAL.Host.Services;

namespace VAL.App.ViewModels
{
    public sealed class CrashWindowViewModel
    {
        private readonly IProcessLauncher _processLauncher;
        private string _logsRoot = string.Empty;

        public CrashWindowViewModel(IProcessLauncher processLauncher)
        {
            _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
            CopyCrashDetailsCommand = new RelayCommand(CopyCrashDetails);
            OpenLogsFolderCommand = new RelayCommand(OpenLogsFolder);
        }

        public string CrashDetails { get; private set; } = string.Empty;
        public RelayCommand CopyCrashDetailsCommand { get; }
        public RelayCommand OpenLogsFolderCommand { get; }

        public void Initialize(CrashWindowRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            CrashDetails = request.CrashDetails ?? string.Empty;
            _logsRoot = request.LogsRoot ?? string.Empty;
        }

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
