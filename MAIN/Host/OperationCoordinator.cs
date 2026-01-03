using System;
using System.Threading;

namespace VAL.Host
{
    internal enum GuardedOperationKind
    {
        Pulse,
        Chronicle
    }

    /// <summary>
    /// Minimal single-flight coordinator for guarded operations.
    /// Ensures at most one long-running operation is active at a time and
    /// provides a shared cancellation token.
    /// </summary>
    internal static class OperationCoordinator
    {
        private static readonly object Gate = new object();
        private static GuardedOperationKind? _currentKind;
        private static CancellationTokenSource? _cts;
        private static long _currentOperationId;

        public static bool IsBusy
        {
            get
            {
                lock (Gate)
                {
                    return _cts != null;
                }
            }
        }

        public static GuardedOperationKind? CurrentKind
        {
            get
            {
                lock (Gate)
                {
                    return _currentKind;
                }
            }
        }

        public static bool IsRunning(GuardedOperationKind kind)
        {
            lock (Gate)
            {
                return _cts != null && _currentKind == kind;
            }
        }

        public static long CurrentOperationId
        {
            get
            {
                lock (Gate)
                {
                    return _cts != null ? _currentOperationId : 0;
                }
            }
        }

        public static bool TryBegin(GuardedOperationKind kind, out CancellationToken token)
        {
            lock (Gate)
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

        public static void End(GuardedOperationKind kind)
        {
            lock (Gate)
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

        public static void RequestCancel()
        {
            lock (Gate)
            {
                try { _cts?.Cancel(); } catch { }
            }
        }

        public static CancellationToken GetTokenIfRunning(GuardedOperationKind kind)
        {
            lock (Gate)
            {
                if (_cts == null) return CancellationToken.None;
                if (_currentKind != kind) return CancellationToken.None;
                return _cts.Token;
            }
        }

        public static bool IsCancellationRequested(GuardedOperationKind kind)
        {
            lock (Gate)
            {
                if (_cts == null) return false;
                if (_currentKind != kind) return false;
                return _cts.IsCancellationRequested;
            }
        }

        public static void ForceEndAll()
        {
            lock (Gate)
            {
                try { _cts?.Dispose(); } catch { }
                _cts = null;
                _currentKind = null;
                _currentOperationId = 0;
            }
        }
    }
}
