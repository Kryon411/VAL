using System;
using VAL.Continuum.Pipeline.Inject;
using VAL.Host.WebMessaging;

namespace VAL.Host.Services
{
    public sealed class ContinuumPump : IContinuumPump
    {
        private readonly ICommandDispatcher _commandDispatcher;
        private readonly IWebMessageSender _webMessageSender;
        private readonly IWebViewRuntime _webViewRuntime;
        private readonly IUiThread _uiThread;
        private readonly IContinuumInjectQueue _injectQueue;

        private IDisposable? _timer;

        public ContinuumPump(
            ICommandDispatcher commandDispatcher,
            IWebMessageSender webMessageSender,
            IWebViewRuntime webViewRuntime,
            IUiThread uiThread,
            IContinuumInjectQueue injectQueue)
        {
            _commandDispatcher = commandDispatcher;
            _webMessageSender = webMessageSender;
            _webViewRuntime = webViewRuntime;
            _uiThread = uiThread;
            _injectQueue = injectQueue;
        }

        public void Start()
        {
            if (_timer != null)
                return;

            _timer = _uiThread.StartTimer(TimeSpan.FromMilliseconds(100), PumpContinuumQueue);
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        private void PumpContinuumQueue()
        {
            if (_webViewRuntime.Core == null)
                return;

            var seed = _injectQueue.Dequeue();
            if (seed == null)
                return;

            try
            {
                var envelope = _commandDispatcher.CreateContinuumInjectEnvelope(seed);
                if (envelope != null)
                    _webMessageSender.Send(envelope);
            }
            catch
            {
                ValLog.Warn(nameof(ContinuumPump), "Continuum dispatch failed.");
            }
        }
    }
}
