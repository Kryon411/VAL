using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;

using VAL.Host;
using VAL.Host.Commands;
using VAL.Host.Logging;
using VAL.Host.Services;
using VAL.Host.Startup;

namespace VAL.App.ViewModels
{
    public sealed class MainWindowViewModel : INotifyPropertyChanged
    {
        private readonly IOperationCoordinator _operationCoordinator;
        private readonly HostCommandRouter _commandRouter;
        private readonly IPortalRuntimeService _portalRuntimeService;
        private readonly IModuleRuntimeService _moduleRuntimeService;
        private readonly IContinuumPump _continuumPump;
        private readonly IDiagnosticsWindowService _diagnosticsWindowService;
        private readonly IToastService _toastService;
        private readonly StartupOptions _startupOptions;
        private readonly ILog _log;
        private readonly NativeNavigationRecoveryService _navigationRecovery;
        private readonly RelayCommand _returnToChatGptCommand;

        public MainWindowViewModel(
            IOperationCoordinator operationCoordinator,
            HostCommandRouter commandRouter,
            IPortalRuntimeService portalRuntimeService,
            IModuleRuntimeService moduleRuntimeService,
            IContinuumPump continuumPump,
            IDiagnosticsWindowService diagnosticsWindowService,
            IToastService toastService,
            StartupOptions startupOptions,
            ILog log,
            NativeNavigationRecoveryService navigationRecovery)
        {
            _operationCoordinator = operationCoordinator;
            _commandRouter = commandRouter;
            _portalRuntimeService = portalRuntimeService;
            _moduleRuntimeService = moduleRuntimeService;
            _continuumPump = continuumPump;
            _diagnosticsWindowService = diagnosticsWindowService;
            _toastService = toastService;
            _startupOptions = startupOptions;
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _navigationRecovery = navigationRecovery ?? throw new ArgumentNullException(nameof(navigationRecovery));

            OpenDiagnosticsCommand = new RelayCommand(() => _diagnosticsWindowService.ShowDiagnostics());
            _returnToChatGptCommand = new RelayCommand(
                () => _navigationRecovery.TryReturnToChatGpt(),
                () => _navigationRecovery.IsAvailable);
        }

        public ICommand OpenDiagnosticsCommand { get; }
        public ICommand ReturnToChatGptCommand => _returnToChatGptCommand;
        public bool IsNavigationRecoveryVisible { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged;

        public void RefreshNavigationAvailability()
        {
            var isAvailable = _navigationRecovery.IsAvailable;
            if (IsNavigationRecoveryVisible == isAvailable)
                return;

            IsNavigationRecoveryVisible = isAvailable;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNavigationRecoveryVisible)));
            _returnToChatGptCommand.RaiseCanExecuteChanged();
        }

        public async Task OnLoadedAsync(Action focusControl)
        {
            try
            {
                _portalRuntimeService.Initialize(focusControl);
            }
            catch
            {
                _log.Warn(nameof(MainWindowViewModel), "Portal runtime initialization failed.");
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
                _log.Warn(nameof(MainWindowViewModel), "Module initialization failed.");
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
                _log.Warn(nameof(MainWindowViewModel), "Portal window attach failed.");
            }
        }

        public void HandleWebMessageJson(VAL.Host.WebMessaging.WebMessageEnvelope envelope)
        {
            try
            {
                var result = _commandRouter.HandleWebMessage(envelope);
                HandleCommandResult(result);
            }
            catch
            {
                _log.Warn(nameof(MainWindowViewModel), "Failed to handle web message.");
                _toastService.ShowMessage(
                    "Command failed.",
                    "The action could not be completed. See Logs/VAL.log for details.",
                    groupKey: "host.command.error",
                    replaceGroup: true,
                    bypassBurstDedupe: true);
            }
        }

        private void HandleCommandResult(HostCommandExecutionResult result)
        {
            if (!result.IsDockInvocation)
                return;

            if (result.IsBlocked)
            {
                _toastService.ShowMessage(
                    result.Reason,
                    null,
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
            _log.Warn(nameof(MainWindowViewModel),
                $"Dock command failed '{commandName}' (reason: {result.Reason}, diagnostic: {diagnostic}, exception: {exception}).");

            _toastService.ShowMessage(
                result.Reason,
                "The action failed. See Logs/VAL.log for details.",
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

                if (!confirmExit())
                    return true;

                _operationCoordinator.RequestCancel();
                return false;
            }
            catch
            {
                _log.Warn(nameof(MainWindowViewModel), "Close guard failed.");
                return false;
            }
        }
    }
}
