using System;
using System.Threading;
using System.Threading.Tasks;

using VAL.Continuum.Pipeline.Inject;
using VAL.Host.Logging;
using VAL.Host.WebMessaging;

namespace VAL.App.Host.Services
{
    public sealed class ContinuumPump : IContinuumPump, IDisposable
    {
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);
        private const int QueueWarnThreshold = 50;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

        private readonly ICommandDispatcher _commandDispatcher;
        private readonly IWebMessageSender _webMessageSender;
        private readonly IWebViewRuntime _webViewRuntime;
        private readonly IUiThread _uiThread;
        private readonly IContinuumInjectInbox _injectQueue;
        private readonly ILog _log;
        private readonly RateLimiter _rateLimiter = new();
        private readonly object _lifecycleGate = new();

        private CancellationTokenSource? _pumpCts;
        private Task? _pumpTask;
        private EssenceInjectController.InjectSeed? _pendingSeed;

        public ContinuumPump(
            ICommandDispatcher commandDispatcher,
            IWebMessageSender webMessageSender,
            IWebViewRuntime webViewRuntime,
            IUiThread uiThread,
            IContinuumInjectInbox injectQueue,
            ILog log)
        {
            _commandDispatcher = commandDispatcher;
            _webMessageSender = webMessageSender;
            _webViewRuntime = webViewRuntime;
            _uiThread = uiThread;
            _injectQueue = injectQueue;
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Start()
        {
            lock (_lifecycleGate)
            {
                if (_pumpTask != null)
                    return;

                _pumpCts = new CancellationTokenSource();
                _pumpTask = Task.Run(() => PumpContinuumQueueAsync(_pumpCts.Token));
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            CancellationTokenSource? cts;
            Task? pumpTask;
            lock (_lifecycleGate)
            {
                cts = _pumpCts;
                pumpTask = _pumpTask;
                _pumpCts = null;
                _pumpTask = null;
            }

            if (cts == null || pumpTask == null)
                return;

            cts.Cancel();

            try
            {
                await pumpTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                cts.Dispose();
            }
        }

        private async Task PumpContinuumQueueAsync(CancellationToken cancellationToken)
        {
            try
            {
                var reader = _injectQueue.Reader;
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (_pendingSeed == null)
                    {
                        if (_webViewRuntime.Core == null)
                        {
                            await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        if (!await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                            break;

                        if (_injectQueue.TryDequeue(out var seed))
                        {
                            _pendingSeed = seed;
                            MaybeLogQueueDepth();
                        }
                    }

                    if (_pendingSeed != null)
                    {
                        if (TryDispatchSeed(_pendingSeed))
                        {
                            _pendingSeed = null;
                        }
                        else
                        {
                            await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }

        private bool TryDispatchSeed(EssenceInjectController.InjectSeed? seed)
        {
            if (seed == null)
                return false;

            var didSend = false;
            _uiThread.Invoke(() =>
            {
                if (_webViewRuntime.Core == null)
                    return;

                try
                {
                    var envelope = _commandDispatcher.CreateContinuumInjectEnvelope(seed);
                    if (envelope != null)
                    {
                        _webMessageSender.Send(envelope);
                        didSend = true;
                    }
                }
                catch
                {
                    var key = "continuum.inject.dispatch.fail";
                    if (_rateLimiter.Allow(key, LogInterval))
                        _log.Warn(nameof(ContinuumPump), "Continuum dispatch failed.");
                }
            });

            return didSend;
        }

        private void MaybeLogQueueDepth()
        {
            var count = _injectQueue.Count;
            if (count < QueueWarnThreshold)
                return;

            var key = "continuum.inject.queue.depth";
            if (!_rateLimiter.Allow(key, LogInterval))
                return;

            _log.Warn(nameof(ContinuumPump), $"Continuum inject queue backlog: {count} items.");
        }

        public void Dispose()
        {
            CancellationTokenSource? cts;
            lock (_lifecycleGate)
            {
                cts = _pumpCts;
                _pumpCts = null;
                _pumpTask = null;
            }

            if (cts == null)
                return;

            try
            {
                cts.Cancel();
            }
            finally
            {
                cts.Dispose();
            }
        }
    }
}
