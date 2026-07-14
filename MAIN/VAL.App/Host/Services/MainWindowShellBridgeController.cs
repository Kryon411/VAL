using System;
using System.Windows;

using VAL.Host;
using VAL.Host.Services;
using VAL.Host.WebMessaging;

namespace VAL.App.Host.Services
{
    public sealed class MainWindowShellBridgeController
    {
        private readonly MainWindowShellStateController _shellStateController;
        private readonly IWebViewRuntime _webViewRuntime;
        private readonly IWebMessageSender _webMessageSender;
        private readonly ILog _log;
        private bool _loggedNotReady;

        public MainWindowShellBridgeController(
            MainWindowShellStateController shellStateController,
            IWebViewRuntime webViewRuntime,
            IWebMessageSender webMessageSender,
            ILog log)
        {
            _shellStateController = shellStateController ?? throw new ArgumentNullException(nameof(shellStateController));
            _webViewRuntime = webViewRuntime ?? throw new ArgumentNullException(nameof(webViewRuntime));
            _webMessageSender = webMessageSender ?? throw new ArgumentNullException(nameof(webMessageSender));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public bool TryHandleLauncherClick(DateTime nowUtc, out bool requiresDockStateSync)
        {
            requiresDockStateSync = false;

            if (!_shellStateController.TryHandleLauncherClick(nowUtc, out var message, out requiresDockStateSync))
            {
                return false;
            }

            TrySendHostMessage(message);
            return true;
        }

        public bool TryHandleDockMessage(WebMessageEnvelope envelope, Rect virtualScreenBounds)
        {
            return _shellStateController.TryApplyDockMessage(envelope, virtualScreenBounds);
        }

        public void PublishLayoutMode()
        {
            TrySendHostMessage(_shellStateController.CreateLayoutModeEnvelope());
            SendDockUiState();
        }

        public void SendDockUiState()
        {
            TrySendHostMessage(_shellStateController.CreateDockUiStateEnvelope());
        }

        private void TrySendHostMessage(MessageEnvelope envelope)
        {
            try
            {
                if (!_webViewRuntime.IsReady)
                {
                    if (_loggedNotReady)
                    {
                        return;
                    }

                    _loggedNotReady = true;
                    _log.Info(nameof(MainWindowShellBridgeController), "Shell message ignored because WebView2 is not ready.");
                    return;
                }

                _loggedNotReady = false;
                _webMessageSender.Send(envelope);
            }
            catch (Exception ex)
            {
                _log.Warn(nameof(MainWindowShellBridgeController), $"Failed to send shell message: {ex.Message}");
            }
        }
    }
}
