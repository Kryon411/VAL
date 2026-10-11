using System.Text.Json;

using VAL.Continuum.Pipeline;
using VAL.Continuum.Pipeline.Truth;
using VAL.Contracts;
using VAL.Host;
using VAL.Host.Services;
using VAL.Host.WebMessaging;

namespace VAL.Continuum
{
    internal sealed class ContinuumChronicleWorkflow : IDisposable
    {
        private const string ToastGroup = "chronicle";
        private readonly object _sync = new();
        private readonly IWebMessageSender _messageSender;
        private readonly IToastHub _toasts;
        private readonly ITruthStore _truthStore;
        private readonly ISessionContext _sessionContext;
        private readonly OperationCoordinator _operations;
        private readonly IToastLedger _toastLedger;
        private readonly ContinuumArchiveService _archives;
        private readonly IBackgroundTaskSupervisor _backgroundTasks;
        private readonly Action<Action> _postToUi;
        private readonly TimeSpan _timeout;
        private readonly Func<bool> _isRefreshInFlight;
        private readonly Action<string> _onAttemptFinished;
        private readonly Action<string> _onCompleted;
        private bool _inFlight;
        private string? _requestId;
        private string? _chatId;
        private CancellationTokenSource? _timeoutCancellation;
        private DateTime _startedUtc = DateTime.MinValue;
        private ToastReason _cancelReason = ToastReason.Background;

        public ContinuumChronicleWorkflow(
            IWebMessageSender messageSender,
            IToastHub toasts,
            ITruthStore truthStore,
            ISessionContext sessionContext,
            OperationCoordinator operations,
            IToastLedger toastLedger,
            ContinuumArchiveService archives,
            IBackgroundTaskSupervisor backgroundTasks,
            Action<Action> postToUi,
            Func<bool> isRefreshInFlight,
            Action<string> onAttemptFinished,
            Action<string> onCompleted,
            TimeSpan? timeout = null)
        {
            _messageSender = messageSender ?? throw new ArgumentNullException(nameof(messageSender));
            _toasts = toasts ?? throw new ArgumentNullException(nameof(toasts));
            _truthStore = truthStore ?? throw new ArgumentNullException(nameof(truthStore));
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _operations = operations ?? throw new ArgumentNullException(nameof(operations));
            _toastLedger = toastLedger ?? throw new ArgumentNullException(nameof(toastLedger));
            _archives = archives ?? throw new ArgumentNullException(nameof(archives));
            _backgroundTasks = backgroundTasks ?? throw new ArgumentNullException(nameof(backgroundTasks));
            _postToUi = postToUi ?? throw new ArgumentNullException(nameof(postToUi));
            _timeout = timeout ?? TimeSpan.FromMinutes(30);
            _isRefreshInFlight = isRefreshInFlight ?? throw new ArgumentNullException(nameof(isRefreshInFlight));
            _onAttemptFinished = onAttemptFinished ?? throw new ArgumentNullException(nameof(onAttemptFinished));
            _onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
        }

        public bool IsInFlight
        {
            get
            {
                lock (_sync)
                    return _inFlight;
            }
        }

        public void Cancel(string? chatId, ToastReason reason)
        {
            try
            {
                if (!_operations.IsRunning(GuardedOperationKind.Chronicle))
                    return;

                string requestId;
                lock (_sync)
                {
                    _cancelReason = reason;
                    requestId = _requestId ?? string.Empty;
                }

                _operations.RequestCancel();
                string? resolvedChatId;
                lock (_sync)
                    resolvedChatId = _chatId;
                if (string.IsNullOrWhiteSpace(resolvedChatId))
                    return;

                _messageSender.Send(new MessageEnvelope
                {
                    Type = WebMessageTypes.Command,
                    Name = WebCommandNames.ContinuumChronicleCancel,
                    ChatId = resolvedChatId,
                    Payload = JsonSerializer.SerializeToElement(new
                    {
                        chatId = resolvedChatId,
                        requestId,
                    }),
                });
            }
            catch
            {
                // Cancellation remains requested even if the client notification fails.
            }
        }

        public void Start(string? chatId, ToastReason reason)
        {
            var resolvedChatId = _sessionContext.ResolveChatId(chatId);
            if (string.IsNullOrWhiteSpace(resolvedChatId) ||
                resolvedChatId.StartsWith("session-", StringComparison.OrdinalIgnoreCase))
            {
                _toasts.TryShow(
                    ToastKey.ChronicleUnavailable,
                    chatId: resolvedChatId,
                    bypassLaunchQuiet: true,
                    origin: ToastOrigin.Continuum,
                    reason: reason);
                return;
            }

            if (_operations.IsBusy ||
                !_operations.TryBegin(GuardedOperationKind.Chronicle, out var chronicleToken))
            {
                ShowOperationInProgress(reason);
                return;
            }

            if (!_sessionContext.IsSessionAttached || _isRefreshInFlight() || !TryMarkStarted(resolvedChatId))
            {
                _operations.End(GuardedOperationKind.Chronicle);
                ShowActionUnavailable(reason);
                return;
            }

            try
            {
                if (!_truthStore.TryBeginTruthRebuild(
                        resolvedChatId,
                        backupExisting: true,
                        out var backupPath,
                        out _,
                        chronicleToken))
                {
                    ResetState();
                    _operations.End(GuardedOperationKind.Chronicle);
                    ShowActionUnavailable(reason);
                    return;
                }

                _archives.TryAppendChronicleAudit(
                    resolvedChatId,
                    $"Chronicle STARTED  | Utc={DateTime.UtcNow:o} | Backup={backupPath}");

                _messageSender.Send(new MessageEnvelope
                {
                    Type = WebMessageTypes.Command,
                    Name = WebCommandNames.ContinuumChronicleStart,
                    ChatId = resolvedChatId,
                    Payload = JsonSerializer.SerializeToElement(new
                    {
                        chatId = resolvedChatId,
                        requestId = GetRequestId(),
                        mode = "full",
                    }),
                });

                ArmTimeout(resolvedChatId);

                _toasts.TryShow(
                    ToastKey.ChronicleStarted,
                    chatId: resolvedChatId,
                    bypassLaunchQuiet: true,
                    origin: ToastOrigin.Continuum,
                    reason: reason);
            }
            catch
            {
                _archives.TryAbortTruthRebuild(resolvedChatId);
                ResetState();
                _operations.End(GuardedOperationKind.Chronicle);
                ShowActionUnavailable(reason, replaceChronicleGroup: true);
            }
        }

        public void Complete(ContinuumCommandMessage message)
        {
            var shouldEndOperation = false;
            string? completionChatId = null;

            try
            {
                var resolvedChatId = message.ChatId;
                if (string.IsNullOrWhiteSpace(resolvedChatId))
                    return;
                completionChatId = resolvedChatId;

                if (!TryTakeActiveRequest(resolvedChatId, message.RequestId, out var startedUtc, out var cancelReason))
                    return;

                shouldEndOperation = true;
                _onAttemptFinished(resolvedChatId);

                try
                {
                    _toastLedger.TryMarkShown(resolvedChatId, "guidance.chronicle_suggested");
                }
                catch
                {
                    // Guidance persistence is best-effort.
                }

                var captured = message.CapturedTurns ?? 0;
                var elapsedMilliseconds = message.ElapsedMilliseconds ??
                    (long)Math.Max(0, (DateTime.UtcNow - startedUtc).TotalMilliseconds);

                if (_operations.IsCancellationRequested(GuardedOperationKind.Chronicle))
                {
                    _archives.TryAbortTruthRebuild(resolvedChatId);
                    _toasts.TryShowOperationCancelled(ToastGroup, ToastOrigin.Continuum, cancelReason);
                    _archives.TryAppendChronicleAudit(
                        resolvedChatId,
                        $"Chronicle CANCELLED | Utc={DateTime.UtcNow:o} | Captured={captured} | Ms={elapsedMilliseconds}");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(message.Error) || captured <= 0)
                {
                    _archives.TryAbortTruthRebuild(resolvedChatId);
                    ShowActionUnavailable(ToastReason.Background, replaceChronicleGroup: true);
                    _archives.TryAppendChronicleAudit(
                        resolvedChatId,
                        $"Chronicle FAILED    | Utc={DateTime.UtcNow:o} | Error={message.Error} | Captured={captured} | Ms={elapsedMilliseconds}");
                    return;
                }

                if (!_truthStore.TryCommitTruthRebuild(resolvedChatId))
                {
                    _archives.TryAbortTruthRebuild(resolvedChatId);
                    ShowActionUnavailable(ToastReason.Background, replaceChronicleGroup: true);
                    _archives.TryAppendChronicleAudit(
                        resolvedChatId,
                        $"Chronicle FAILED    | Utc={DateTime.UtcNow:o} | Error=commit_failed | Captured={captured} | Ms={elapsedMilliseconds}");
                    return;
                }

                _archives.TryDeleteDerivedArtifacts(resolvedChatId);
                _archives.WriteChronicleMarker(resolvedChatId);
                try
                {
                    _sessionContext.MarkChronicleRebuilt(resolvedChatId);
                }
                catch
                {
                    // The archive itself remains the source of truth.
                }

                _onCompleted(resolvedChatId);
                _toasts.TryShow(
                    ToastKey.ChronicleCompleted,
                    chatId: resolvedChatId,
                    bypassLaunchQuiet: true,
                    origin: ToastOrigin.Continuum,
                    reason: ToastReason.Background);
                _archives.TryAppendChronicleAudit(
                    resolvedChatId,
                    $"Chronicle COMPLETED | Utc={DateTime.UtcNow:o} | Captured={captured} | Ms={elapsedMilliseconds}");
            }
            catch
            {
                ResetState();
                if (!string.IsNullOrWhiteSpace(completionChatId))
                    _archives.TryAbortTruthRebuild(completionChatId);

                try
                {
                    ShowActionUnavailable(ToastReason.Background, replaceChronicleGroup: true);
                }
                catch
                {
                    // Completion cleanup cannot surface another failure.
                }
            }
            finally
            {
                if (shouldEndOperation)
                    _operations.End(GuardedOperationKind.Chronicle);
            }
        }

        private bool TryMarkStarted(string chatId)
        {
            lock (_sync)
            {
                if (_inFlight)
                    return false;

                _inFlight = true;
                _requestId = Guid.NewGuid().ToString("N");
                _chatId = chatId;
                _timeoutCancellation = new CancellationTokenSource();
                _startedUtc = DateTime.UtcNow;
                _cancelReason = ToastReason.Background;
                return true;
            }
        }

        private bool TryTakeActiveRequest(
            string chatId,
            string? requestId,
            out DateTime startedUtc,
            out ToastReason cancelReason)
        {
            lock (_sync)
            {
                startedUtc = _startedUtc;
                cancelReason = _cancelReason;
                if (!_inFlight ||
                    !string.Equals(chatId, _chatId, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(requestId) ||
                    !string.Equals(requestId, _requestId, StringComparison.Ordinal))
                {
                    return false;
                }

                ResetStateLocked();
                return true;
            }
        }

        private string GetRequestId()
        {
            lock (_sync)
                return _requestId ?? Guid.NewGuid().ToString("N");
        }

        private void ArmTimeout(string chatId)
        {
            string requestId;
            CancellationToken token;
            lock (_sync)
            {
                requestId = _requestId!;
                token = _timeoutCancellation!.Token;
            }

            _backgroundTasks.Run("Chronicle completion timeout", async cancellationToken =>
            {
                await Task.Delay(_timeout, cancellationToken).ConfigureAwait(false);
                _postToUi(() =>
                {
                    if (!TryTakeActiveRequest(chatId, requestId, out _, out _))
                        return;
                    try
                    {
                        _archives.TryAbortTruthRebuild(chatId);
                        _archives.TryAppendChronicleAudit(chatId, $"Chronicle FAILED | Utc={DateTime.UtcNow:o} | Error=timeout");
                        ShowActionUnavailable(ToastReason.Background, replaceChronicleGroup: true);
                    }
                    finally
                    {
                        _operations.End(GuardedOperationKind.Chronicle);
                    }
                });
            }, cancellationToken: token);
        }

        private void ResetState()
        {
            lock (_sync)
                ResetStateLocked();
        }

        private void ResetStateLocked()
        {
            _timeoutCancellation?.Cancel();
            _timeoutCancellation?.Dispose();
            _timeoutCancellation = null;
            _inFlight = false;
            _requestId = null;
            _chatId = null;
            _startedUtc = DateTime.MinValue;
            _cancelReason = ToastReason.Background;
        }

        private void ShowOperationInProgress(ToastReason reason)
        {
            _toasts.TryShow(
                ToastKey.OperationInProgress,
                bypassLaunchQuiet: true,
                origin: ToastOrigin.Continuum,
                reason: reason);
        }

        public void Dispose()
        {
            string? chatId;
            lock (_sync)
            {
                chatId = _chatId;
                ResetStateLocked();
            }
            if (!string.IsNullOrWhiteSpace(chatId))
            {
                _archives.TryAbortTruthRebuild(chatId);
                _operations.End(GuardedOperationKind.Chronicle);
            }
        }

        private void ShowActionUnavailable(ToastReason reason, bool replaceChronicleGroup = false)
        {
            _toasts.TryShow(
                ToastKey.ActionUnavailable,
                bypassLaunchQuiet: true,
                groupKeyOverride: replaceChronicleGroup ? ToastGroup : null,
                replaceGroupOverride: replaceChronicleGroup,
                bypassBurstDedupeOverride: replaceChronicleGroup,
                origin: ToastOrigin.Continuum,
                reason: reason);
        }
    }
}
