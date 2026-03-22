using System;
using System.Threading;
using VAL.Host.Services;

namespace VAL.Host
{
    public enum GuardedOperationKind
    {
        Pulse,
        Chronicle
    }

    /// <summary>
    /// Minimal single-flight coordinator for guarded operations.
    /// Ensures at most one long-running operation is active at a time and
    /// provides a shared cancellation token.
    /// </summary>
    public sealed class OperationCoordinator : IOperationCoordinator, IDisposable
    {
        private readonly object _gate = new object();
        private GuardedOperationKind? _currentKind;
        private CancellationTokenSource? _cts;
        private long _currentOperationId;

        public bool IsBusy
        {
            get
            {
                lock (_gate)
                {
                    return _cts != null;
                }
            }
        }

        public GuardedOperationKind? CurrentKind
        {
            get
            {
                lock (_gate)
                {
                    return _currentKind;
                }
            }
        }

        public bool IsRunning(GuardedOperationKind kind)
        {
            lock (_gate)
            {
                return _cts != null && _currentKind == kind;
            }
        }

        public long CurrentOperationId
        {
            get
            {
                lock (_gate)
                {
                    return _cts != null ? _currentOperationId : 0;
                }
            }
        }

        public bool TryBegin(GuardedOperationKind kind, out CancellationToken token)
        {
            lock (_gate)
            {
                if (_cts != null)
                {
                    token = CancellationToken.None;
                    return false;
                }

                _currentKind = kind;
                _currentOperationId++;
                _cts = new CancellationTokenSource();
                token = _cts.Token;
                return true;
            }
        }

        public void End(GuardedOperationKind kind)
        {
            lock (_gate)
            {
                if (_cts == null)
                    return;

                if (_currentKind != kind)
                    return;

                try { _cts.Dispose(); } catch { }
                _cts = null;
                _currentKind = null;
            }
        }

        public void RequestCancel()
        {
            lock (_gate)
            {
                try { _cts?.Cancel(); } catch { }
            }
        }

        public CancellationToken GetTokenIfRunning(GuardedOperationKind kind)
        {
            lock (_gate)
            {
                if (_cts == null) return CancellationToken.None;
                if (_currentKind != kind) return CancellationToken.None;
                return _cts.Token;
            }
        }

        public bool IsCancellationRequested(GuardedOperationKind kind)
        {
            lock (_gate)
            {
                if (_cts == null) return false;
                if (_currentKind != kind) return false;
                return _cts.IsCancellationRequested;
            }
        }

        public void ForceEndAll()
        {
            lock (_gate)
            {
                try { _cts?.Dispose(); } catch { }
                _cts = null;
                _currentKind = null;
                _currentOperationId = 0;
            }
        }

        public void Dispose()
        {
            ForceEndAll();
        }
    }
}
