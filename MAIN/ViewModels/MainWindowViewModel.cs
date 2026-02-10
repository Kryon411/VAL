using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using VAL.Host;
using VAL.Host.Services;
using VAL.Host.Commands;
using VAL.Host.Logging;
using VAL.Host.Startup;

namespace VAL.ViewModels
{
    public sealed class MainWindowViewModel : INotifyPropertyChanged
    {
        private readonly IOperationCoordinator _operationCoordinator;
        private readonly ICommandDispatcher _commandDispatcher;
        private readonly IPortalRuntimeService _portalRuntimeService;
        private readonly IModuleRuntimeService _moduleRuntimeService;
        private readonly IContinuumPump _continuumPump;
        private readonly IProcessLauncher _processLauncher;
        private readonly IAppPaths _appPaths;
        private readonly IDiagnosticsWindowService _diagnosticsWindowService;
        private readonly ITruthHealthWindowService _truthHealthWindowService;
        private readonly StartupOptions _startupOptions;

        private long _lastExitWarnedOperationId;
        private bool _isDockOpen;
        private string _statusText = string.Empty;
        private bool _isDebugEnabled;

        public MainWindowViewModel(
            IOperationCoordinator operationCoordinator,
            ICommandDispatcher commandDispatcher,
            IPortalRuntimeService portalRuntimeService,
            IModuleRuntimeService moduleRuntimeService,
            IContinuumPump continuumPump,
            IProcessLauncher processLauncher,
            IAppPaths appPaths,
            IDiagnosticsWindowService diagnosticsWindowService,
            ITruthHealthWindowService truthHealthWindowService,
            StartupOptions startupOptions)
        {
            _operationCoordinator = operationCoordinator;
            _commandDispatcher = commandDispatcher;
            _portalRuntimeService = portalRuntimeService;
            _moduleRuntimeService = moduleRuntimeService;
            _continuumPump = continuumPump;
            _processLauncher = processLauncher;
            _appPaths = appPaths;
            _diagnosticsWindowService = diagnosticsWindowService;
            _truthHealthWindowService = truthHealthWindowService;
            _startupOptions = startupOptions;

            ToggleDockCommand = new RelayCommand(() => IsDockOpen = !IsDockOpen);
            OpenLogsFolderCommand = new RelayCommand(() => _processLauncher.OpenFolder(_appPaths.LogsRoot));
            OpenDataFolderCommand = new RelayCommand(() => _processLauncher.OpenFolder(_appPaths.DataRoot));
            OpenModulesFolderCommand = new RelayCommand(() => _processLauncher.OpenFolder(_appPaths.ModulesRoot));
            OpenProfileFolderCommand = new RelayCommand(() => _processLauncher.OpenFolder(_appPaths.ProfileRoot));
            OpenDiagnosticsCommand = new RelayCommand(() => _diagnosticsWindowService.ShowDiagnostics());
            OpenTruthHealthCommand = new RelayCommand(() => _truthHealthWindowService.ShowTruthHealth());
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ICommand ToggleDockCommand { get; }
        public ICommand OpenLogsFolderCommand { get; }
        public ICommand OpenDataFolderCommand { get; }
        public ICommand OpenModulesFolderCommand { get; }
        public ICommand OpenProfileFolderCommand { get; }
        public ICommand OpenDiagnosticsCommand { get; }
        public ICommand OpenTruthHealthCommand { get; }

        public bool IsDockOpen
        {
            get => _isDockOpen;
            set => SetProperty(ref _isDockOpen, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public bool IsDebugEnabled
        {
            get => _isDebugEnabled;
            set => SetProperty(ref _isDebugEnabled, value);
        }

        public async Task OnLoadedAsync(Action focusControl)
        {
            try
            {
                _portalRuntimeService.Initialize(focusControl);
            }
            catch
            {
                ValLog.Warn(nameof(MainWindowViewModel), "Portal runtime initialization failed.");
            }

            if (_startupOptions.SafeMode)
                return;

            _moduleRuntimeService.Start();
            _continuumPump.Start();

            try
            {
                await _moduleRuntimeService.EnsureModulesInitializedAsync();
            }
            catch
            {
                ValLog.Warn(nameof(MainWindowViewModel), "Module initialization failed.");
            }
        }

        public void AttachPortalWindow(IntPtr hwnd)
        {
            try
            {
                _portalRuntimeService.AttachWindow(hwnd);
            }
            catch
            {
                ValLog.Warn(nameof(MainWindowViewModel), "Portal window attach failed.");
            }
        }

        public void HandleWebMessageJson(VAL.Host.WebMessaging.WebMessageEnvelope envelope)
        {
            try
            {
                var result = _commandDispatcher.HandleWebMessageJson(envelope);
                HandleCommandResult(result);
            }
            catch
            {
                ValLog.Warn(nameof(MainWindowViewModel), "Failed to handle web message.");
                ToastManager.ShowCatalog(
                    "Command failed.",
                    "The action could not be completed. See Logs/VAL.log for details.",
                    ToastManager.ToastDurationBucket.M,
                    groupKey: "host.command.error",
                    replaceGroup: true,
                    bypassBurstDedupe: true);
            }
        }

        private static void HandleCommandResult(HostCommandExecutionResult result)
        {
            if (!result.IsDockInvocation)
                return;

            if (result.IsBlocked)
            {
                ToastManager.ShowCatalog(
                    result.Reason,
                    null,
                    ToastManager.ToastDurationBucket.M,
                    groupKey: "host.command.blocked",
                    replaceGroup: true,
                    bypassBurstDedupe: true);
                return;
            }

            if (!result.IsError)
                return;

            var commandName = string.IsNullOrWhiteSpace(result.CommandName) ? "<unknown>" : result.CommandName;
            var diagnostic = string.IsNullOrWhiteSpace(result.DiagnosticDetail) ? "none" : result.DiagnosticDetail;
            var exception = result.Exception == null ? "none" : LogSanitizer.Sanitize(result.Exception.ToString());
            ValLog.Warn(nameof(MainWindowViewModel),
                $"Dock command failed '{commandName}' (reason: {result.Reason}, diagnostic: {diagnostic}, exception: {exception}).");

            ToastManager.ShowCatalog(
                result.Reason,
                "The action failed. See Logs/VAL.log for details.",
                ToastManager.ToastDurationBucket.M,
                groupKey: "host.command.error",
                replaceGroup: true,
                bypassBurstDedupe: true);
        }

        public bool ShouldCancelClose(Func<bool> confirmExit)
        {
            try
            {
                if (!_operationCoordinator.IsBusy)
                    return false;

                var opId = _operationCoordinator.CurrentOperationId;
                if (opId != 0 && opId == _lastExitWarnedOperationId)
                {
                    _operationCoordinator.RequestCancel();
                    return false;
                }

                _lastExitWarnedOperationId = opId;

                if (!confirmExit())
                    return true;

                _operationCoordinator.RequestCancel();
                return false;
            }
            catch
            {
                ValLog.Warn(nameof(MainWindowViewModel), "Close guard failed.");
                return false;
            }
        }

        private void SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value))
                return;

            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
