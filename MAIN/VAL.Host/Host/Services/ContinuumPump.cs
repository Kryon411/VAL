using System;
using System.Threading;
using System.Threading.Tasks;
using VAL.Continuum.Pipeline.Inject;
using VAL.Host.Logging;
using VAL.Host.WebMessaging;

namespace VAL.Host.Services
{
    public sealed class ContinuumPump : IContinuumPump, IDisposable
    {
        private static readonly RateLimiter RateLimiter = new();
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);
        private const int QueueWarnThreshold = 50;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

        private readonly ICommandDispatcher _commandDispatcher;
        private readonly IWebMessageSender _webMessageSender;
        private readonly IWebViewRuntime _webViewRuntime;
        private readonly IUiThread _uiThread;
        private readonly IContinuumInjectInbox _injectQueue;

        private CancellationTokenSource? _pumpCts;
        private Task? _pumpTask;
        private EssenceInjectController.InjectSeed? _pendingSeed;

        public ContinuumPump(
            ICommandDispatcher commandDispatcher,
            IWebMessageSender webMessageSender,
            IWebViewRuntime webViewRuntime,
            IUiThread uiThread,
            IContinuumInjectInbox injectQueue)
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

        public void StopPump()
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
            finally
            {
                cts.Dispose();
            }

            _pumpTask = null;
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
                    if (RateLimiter.Allow(key, LogInterval))
                        ValLog.Warn(nameof(ContinuumPump), "Continuum dispatch failed.");
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
            if (!RateLimiter.Allow(key, LogInterval))
                return;

            ValLog.Warn(nameof(ContinuumPump), $"Continuum inject queue backlog: {count} items.");
        }

        public void Dispose()
        {
            StopPump();
        }
    }
}
