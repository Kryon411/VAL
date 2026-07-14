using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using VAL.Host.Logging;

namespace VAL.Host.Services
{
    public sealed class BackgroundTaskSupervisor : IBackgroundTaskSupervisor, IDisposable
    {
        private readonly ConcurrentDictionary<long, Task> _activeTasks = new();
        private readonly CancellationTokenSource _shutdown = new();
        private readonly object _lifecycleGate = new();
        private readonly ILog _log;
        private long _nextTaskId;
        private int _disposed;
        private bool _stopping;

        public BackgroundTaskSupervisor(ILog log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public int ActiveCount => _activeTasks.Count;

        public void Run(
            string operation,
            Func<CancellationToken, Task> work,
            Action<Exception>? onError = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(work);

            lock (_lifecycleGate)
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                if (_stopping)
                    throw new InvalidOperationException("The background task supervisor is stopping.");

                var operationName = string.IsNullOrWhiteSpace(operation)
                    ? "background operation"
                    : operation.Trim();
                var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _shutdown.Token);
                var taskId = Interlocked.Increment(ref _nextTaskId);
                var task = Task.Run(
                    () => work(linkedCancellation.Token),
                    linkedCancellation.Token);

                _activeTasks[taskId] = task;
                _ = ObserveAsync(taskId, operationName, task, linkedCancellation, onError);
            }
        }

        public async Task StopAsync(TimeSpan timeout)
        {
            Task[] snapshot;
            lock (_lifecycleGate)
            {
                _stopping = true;
                _shutdown.Cancel();
                snapshot = _activeTasks.Values.ToArray();
            }

            if (snapshot.Length == 0)
                return;

            var completion = Task.WhenAll(snapshot);
            var effectiveTimeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(2) : timeout;

            try
            {
                await completion.WaitAsync(effectiveTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _log.Warn(nameof(BackgroundTaskSupervisor),
                    $"Timed out waiting for {_activeTasks.Count} background operation(s) to stop.");
            }
            catch
            {
                // Each task is observed and logged by ObserveAsync.
            }
        }

        private async Task ObserveAsync(
            long taskId,
            string operation,
            Task task,
            CancellationTokenSource linkedCancellation,
            Action<Exception>? onError)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
            {
                // Expected cooperative cancellation.
            }
            catch (Exception ex)
            {
                _log.Warn(nameof(BackgroundTaskSupervisor),
                    $"{operation} failed. {ex.GetType().Name}: {LogSanitizer.Sanitize(ex.Message)}");
                _log.LogError(nameof(BackgroundTaskSupervisor), ex.ToString());

                try
                {
                    onError?.Invoke(ex);
                }
                catch (Exception callbackException)
                {
                    _log.Warn(nameof(BackgroundTaskSupervisor),
                        $"{operation} error callback failed. {callbackException.GetType().Name}.");
                }
            }
            finally
            {
                _activeTasks.TryRemove(taskId, out _);
                linkedCancellation.Dispose();
            }
        }

        public void Dispose()
        {
            lock (_lifecycleGate)
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;

                _stopping = true;
                _shutdown.Cancel();
            }

            _shutdown.Dispose();
        }
    }
}
