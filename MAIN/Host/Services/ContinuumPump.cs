using System;
using System.Threading;
using System.Threading.Tasks;
using VAL.Continuum.Pipeline.Inject;
using VAL.Host.Logging;
using VAL.Host.WebMessaging;

namespace VAL.Host.Services
{
    public sealed class ContinuumPump : IContinuumPump
    {
        private static readonly RateLimiter RateLimiter = new();
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);
        private const int QueueWarnThreshold = 50;

        private readonly ICommandDispatcher _commandDispatcher;
        private readonly IWebMessageSender _webMessageSender;
        private readonly IWebViewRuntime _webViewRuntime;
        private readonly IUiThread _uiThread;
        private readonly IContinuumInjectQueue _injectQueue;

        private CancellationTokenSource? _pumpCts;
        private Task? _pumpTask;

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
            if (_pumpTask != null)
                return;

            _pumpCts = new CancellationTokenSource();
            _pumpTask = Task.Run(() => PumpContinuumQueueAsync(_pumpCts.Token));
        }

        public void Stop()
        {
            var cts = _pumpCts;
            if (cts == null)
                return;

            _pumpCts = null;
            cts.Cancel();

            try
            {
                _pumpTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // ignore
            }

            _pumpTask = null;
            FlushPendingSeeds();
        }

        private async Task PumpContinuumQueueAsync(CancellationToken cancellationToken)
        {
            try
            {
                var reader = _injectQueue.Reader;
                while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (_injectQueue.TryDequeue(out var seed))
                    {
                        MaybeLogQueueDepth();
                        DispatchSeed(seed);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }

        private void FlushPendingSeeds()
        {
            while (_injectQueue.TryDequeue(out var seed))
            {
                DispatchSeed(seed);
            }
        }

        private void DispatchSeed(EssenceInjectController.InjectSeed? seed)
        {
            if (seed == null)
                return;

            _uiThread.Invoke(() =>
            {
                if (_webViewRuntime.Core == null)
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
            });
        }

        private void MaybeLogQueueDepth()
        {
            var count = _injectQueue.Count;
            if (count < QueueWarnThreshold)
                return;

            var key = "continuum.inject.queue.depth";
            if (!RateLimiter.Allow(key, LogInterval))
                return;

            ValLog.Warn(nameof(ContinuumPump), $"Continuum inject queue backlog: {count} items.");
        }
    }
}
