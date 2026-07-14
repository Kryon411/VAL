using System;

using VAL.Host;

namespace VAL.App.Host.Services
{
    public sealed class MainWindowShellTimingController
    {
        private static readonly TimeSpan PersistDelay = TimeSpan.FromMilliseconds(150);
        private static readonly TimeSpan DockStateSyncDelay = TimeSpan.FromMilliseconds(450);
        private readonly MainWindowShellStateController _shellStateController;
        private readonly MainWindowShellBridgeController _shellBridgeController;
        private readonly IDeferredAction _persistAction;
        private readonly IDeferredAction _dockStateSyncAction;
        private readonly ILog _log;

        public MainWindowShellTimingController(
            IDeferredActionFactory deferredActionFactory,
            MainWindowShellStateController shellStateController,
            MainWindowShellBridgeController shellBridgeController,
            ILog log)
        {
            ArgumentNullException.ThrowIfNull(deferredActionFactory);

            _shellStateController = shellStateController ?? throw new ArgumentNullException(nameof(shellStateController));
            _shellBridgeController = shellBridgeController ?? throw new ArgumentNullException(nameof(shellBridgeController));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _persistAction = deferredActionFactory.Create(PersistDelay, PersistState);
            _dockStateSyncAction = deferredActionFactory.Create(DockStateSyncDelay, _shellBridgeController.SendDockUiState);
        }

        public void LoadState()
        {
            try
            {
                _shellStateController.Load();
            }
            catch
            {
                _log.Warn(nameof(MainWindowShellTimingController), "Failed to load shell state.");
            }
        }

        public void ScheduleStatePersist()
        {
            _persistAction.Restart();
        }

        public void RequestDockStateSync()
        {
            _dockStateSyncAction.Restart();
        }

        public void FlushAndStop()
        {
            _persistAction.Cancel();
            _dockStateSyncAction.Cancel();

            try
            {
                _shellStateController.Save();
            }
            catch
            {
                _log.Warn(nameof(MainWindowShellTimingController), "Failed to persist shell state during shutdown.");
            }
        }

        private void PersistState()
        {
            try
            {
                _shellStateController.Save();
            }
            catch
            {
                _log.Warn(nameof(MainWindowShellTimingController), "Failed to persist shell state.");
            }
        }
    }
}
