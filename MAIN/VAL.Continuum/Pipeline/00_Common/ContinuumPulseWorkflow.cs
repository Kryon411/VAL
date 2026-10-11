using System.Text.Json;

using VAL.Continuum.Pipeline;
using VAL.Continuum.Pipeline.Inject;
using VAL.Continuum.Pipeline.QuickRefresh;
using VAL.Continuum.Pipeline.Signal;
using VAL.Contracts;
using VAL.Host;
using VAL.Host.Services;
using VAL.Host.WebMessaging;

namespace VAL.Continuum
{
    internal sealed class ContinuumPulseWorkflow : IDisposable
    {
        private static readonly TimeSpan GuidanceSuppressWindow = TimeSpan.FromSeconds(25);
        private static readonly TimeSpan RefreshCooldown = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan FlushAckTimeout = TimeSpan.FromMilliseconds(900);
        private static readonly TimeSpan SignalResponseTimeout = TimeSpan.FromSeconds(45);

        private readonly object _sync = new();
        private readonly IWebMessageSender _messageSender;
        private readonly IToastHub _toasts;
        private readonly IQuickRefreshService _quickRefresh;
        private readonly IContinuumInjectInbox _injectQueue;
        private readonly ISessionContext _sessionContext;
        private readonly OperationCoordinator _operations;
        private readonly IBackgroundTaskSupervisor _backgroundTasks;
        private readonly ContinuumArchiveService _archives;
        private readonly Func<bool> _isChronicleInFlight;
        private readonly Action<Action> _postToUi;
        private readonly TimeSpan _operationTimeout;
        private CancellationTokenSource? _lifetimeCancellation;
        private long _refreshOperationId;
        private string? _injectionRequestId;
        private string? _pendingPulseChatId;
        private string? _pendingFlushRequestId;
        private bool _refreshInFlight;
        private string? _refreshChatId;
        private DateTime _lastRefreshCompletedUtc = DateTime.MinValue;
        private DateTime _suppressGuidanceUntilUtc = DateTime.MinValue;
        private PendingSignalPulse? _pendingSignalPulse;

        public ContinuumPulseWorkflow(
            IWebMessageSender messageSender,
            IToastHub toasts,
            IQuickRefreshService quickRefresh,
            IContinuumInjectInbox injectQueue,
            ISessionContext sessionContext,
            OperationCoordinator operations,
            IBackgroundTaskSupervisor backgroundTasks,
            ContinuumArchiveService archives,
            Func<bool> isChronicleInFlight,
            Action<Action> postToUi,
            TimeSpan? operationTimeout = null)
        {
            _messageSender = messageSender ?? throw new ArgumentNullException(nameof(messageSender));
            _toasts = toasts ?? throw new ArgumentNullException(nameof(toasts));
            _quickRefresh = quickRefresh ?? throw new ArgumentNullException(nameof(quickRefresh));
            _injectQueue = injectQueue ?? throw new ArgumentNullException(nameof(injectQueue));
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _operations = operations ?? throw new ArgumentNullException(nameof(operations));
            _backgroundTasks = backgroundTasks ?? throw new ArgumentNullException(nameof(backgroundTasks));
            _archives = archives ?? throw new ArgumentNullException(nameof(archives));
            _isChronicleInFlight = isChronicleInFlight ?? throw new ArgumentNullException(nameof(isChronicleInFlight));
            _postToUi = postToUi ?? throw new ArgumentNullException(nameof(postToUi));
            _operationTimeout = operationTimeout ?? TimeSpan.FromMinutes(3);
            if (_operationTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(operationTimeout));
        }

        public bool IsInFlight
        {
            get
            {
                lock (_sync)
                    return _refreshInFlight;
            }
        }

        public bool IsGuidanceSuppressed(DateTime utcNow)
        {
            lock (_sync)
            {
                return _refreshInFlight ||
                       (_suppressGuidanceUntilUtc != DateTime.MinValue && utcNow < _suppressGuidanceUntilUtc);
            }
        }

        public void Start(string? chatId, ToastReason reason)
        {
            var resolvedChatId = _sessionContext.ResolveChatId(chatId);
            if (string.IsNullOrWhiteSpace(resolvedChatId) ||
                resolvedChatId.StartsWith("session-", StringComparison.OrdinalIgnoreCase))
            {
                _toasts.TryShow(
                    ToastKey.PulseUnavailable,
                    chatId: resolvedChatId,
                    bypassLaunchQuiet: true,
                    origin: ToastOrigin.Continuum,
                    reason: reason);
                return;
            }

            if (_operations.IsBusy)
            {
                ShowOperationInProgress(reason);
                return;
            }

            if (!_archives.HasTruthLog(resolvedChatId))
            {
                if (!_isChronicleInFlight())
                {
                    _toasts.TryShow(
                        ToastKey.PulseNoTruthLogFound,
                        chatId: resolvedChatId,
                        bypassLaunchQuiet: true,
                        origin: ToastOrigin.Continuum,
                        reason: reason);
                }

                return;
            }

            if (!_operations.TryBegin(GuardedOperationKind.Pulse, out _))
            {
                ShowOperationInProgress(reason);
                return;
            }

            if (!TryBeginRefresh(resolvedChatId, reason))
            {
                _operations.End(GuardedOperationKind.Pulse);
                return;
            }

            StartLifetimeWatch(resolvedChatId);

            _toasts.TryShow(
                ToastKey.PulseInitiated,
                chatId: resolvedChatId,
                bypassLaunchQuiet: true,
                origin: ToastOrigin.Continuum,
                reason: reason);

            if (!RequestCaptureFlushAndArmPulse(resolvedChatId))
                RunPulseNow(resolvedChatId);
        }

        public void HandleEvent(ContinuumCommandMessage message)
        {
            var eventName = (message.Event ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(eventName))
                return;

            if (ContinuumEventParser.TryParseSignalReplySettled(eventName, out _))
            {
                HandleSignalReplySettled(message);
                return;
            }

            if (eventName.StartsWith("signal.send.failed:", StringComparison.OrdinalIgnoreCase))
            {
                HandleSignalFailure(message.ChatId, message.RequestId);
                return;
            }

            if (eventName.StartsWith("refresh.inject.success", StringComparison.OrdinalIgnoreCase))
                HandleInjectSuccessEvent(message.ChatId, message.RequestId, eventName);
            else if (eventName.StartsWith("refresh.inject.failed:", StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetPendingSignalPulse(message.ChatId ?? string.Empty, out var signal) &&
                    string.Equals(message.RequestId, signal.SignalRequestId, StringComparison.Ordinal))
                {
                    HandleSignalFailure(message.ChatId, message.RequestId);
                    return;
                }
                bool matches;
                lock (_sync)
                    matches = MatchesInjection(message.RequestId);
                if (matches)
                {
                    FinishRefresh(message.ChatId!, showReadyToast: false);
                    ShowActionUnavailable(message.ChatId, ToastReason.Background);
                }
            }
        }

        public void HandleCaptureFlushAck(ContinuumCommandMessage message)
        {
            try
            {
                var requestId = (message.RequestId ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(requestId))
                    return;

                string? resolvedChatId;
                string? pendingRequestId;
                lock (_sync)
                {
                    resolvedChatId = message.ChatId;
                    pendingRequestId = _pendingFlushRequestId;
                    if (string.IsNullOrWhiteSpace(resolvedChatId) ||
                        !string.Equals(resolvedChatId, _pendingPulseChatId, StringComparison.OrdinalIgnoreCase) ||
                        !requestId.Equals(pendingRequestId, StringComparison.Ordinal))
                        return;

                    _pendingPulseChatId = null;
                    _pendingFlushRequestId = null;
                }

                if (!_operations.IsRunning(GuardedOperationKind.Pulse))
                    return;

                if (!string.IsNullOrWhiteSpace(message.Error))
                {
                    FinishRefresh(resolvedChatId, showReadyToast: false);
                    ShowActionUnavailable(resolvedChatId, ToastReason.Background);
                    return;
                }

                if (_operations.IsCancellationRequested(GuardedOperationKind.Pulse))
                {
                    FinishRefresh(resolvedChatId, showReadyToast: false);
                    ShowOperationCancelled();
                    return;
                }

                RunPulseNow(resolvedChatId);
            }
            catch
            {
                // Invalid or stale acknowledgements are ignored.
            }
        }

        public void CompleteInjection(string? chatId, string? requestId)
        {
            lock (_sync)
            {
                if (!MatchesInjection(requestId) || _pendingSignalPulse != null ||
                    string.IsNullOrWhiteSpace(chatId) || !_sessionContext.IsValidChatId(chatId))
                    return;
            }
            FinishRefresh(chatId, showReadyToast: true);
        }

        private bool MatchesInjection(string? requestId) =>
            _refreshInFlight && !string.IsNullOrWhiteSpace(requestId) &&
            requestId.Equals(_injectionRequestId, StringComparison.Ordinal) &&
            _operations.IsRunning(GuardedOperationKind.Pulse) &&
            _operations.CurrentOperationId == _refreshOperationId &&
            !_operations.IsCancellationRequested(GuardedOperationKind.Pulse);

        private bool TryBeginRefresh(string chatId, ToastReason reason)
        {
            lock (_sync)
            {
                if (!_sessionContext.IsSessionAttached)
                {
                    ShowActionUnavailable(chatId, reason);
                    return false;
                }

                if (_refreshInFlight)
                {
                    _toasts.TryShow(
                        ToastKey.PulseAlreadyRunning,
                        chatId: chatId,
                        bypassLaunchQuiet: true,
                        origin: ToastOrigin.Continuum,
                        reason: reason);
                    return false;
                }

                if (_lastRefreshCompletedUtc != DateTime.MinValue &&
                    DateTime.UtcNow - _lastRefreshCompletedUtc < RefreshCooldown)
                {
                    ShowActionUnavailable(chatId, reason);
                    return false;
                }

                _refreshInFlight = true;
                _refreshChatId = chatId;
                _refreshOperationId = _operations.CurrentOperationId;
                _injectionRequestId = Guid.NewGuid().ToString("N");
                _suppressGuidanceUntilUtc = DateTime.UtcNow + GuidanceSuppressWindow;
                return true;
            }
        }

        private void FinishRefresh(string chatId, bool showReadyToast)
        {
            var endedRefresh = false;
            PendingSignalPulse? pendingSignal = null;
            CancellationTokenSource? lifetime = null;
            string? injectionRequestId = null;
            lock (_sync)
            {
                if (_refreshInFlight)
                {
                    _refreshInFlight = false;
                    _refreshChatId = null;
                    _refreshOperationId = 0;
                    injectionRequestId = _injectionRequestId;
                    _injectionRequestId = null;
                    _pendingPulseChatId = null;
                    _pendingFlushRequestId = null;
                    lifetime = _lifetimeCancellation;
                    _lifetimeCancellation = null;
                    _lastRefreshCompletedUtc = DateTime.UtcNow;
                    pendingSignal = _pendingSignalPulse;
                    _pendingSignalPulse = null;
                    endedRefresh = true;
                }
            }

            CancelTimeout(pendingSignal);
            lifetime?.Cancel();
            lifetime?.Dispose();
            CancelClientInjection(injectionRequestId);
            CancelClientInjection(pendingSignal?.SignalRequestId);

            if (endedRefresh)
                _operations.End(GuardedOperationKind.Pulse);

            if (endedRefresh && showReadyToast && !string.IsNullOrWhiteSpace(chatId))
            {
                _toasts.TryShow(
                    ToastKey.PulseReady,
                    chatId: chatId,
                    bypassLaunchQuiet: true,
                    origin: ToastOrigin.Continuum,
                    reason: ToastReason.Background);
            }
        }

        private void HandleInjectSuccessEvent(string? chatId, string? requestId, string eventName)
        {
            var resolvedChatId = _sessionContext.ResolveChatId(chatId);
            if (string.IsNullOrWhiteSpace(resolvedChatId))
                return;

            if (ContinuumEventParser.TryParseRefreshInjectSuccess(eventName, out var mode, out var label))
            {
                if (mode.Equals("Signal", StringComparison.OrdinalIgnoreCase))
                    return;

                if (mode.Equals("Pulse", StringComparison.OrdinalIgnoreCase))
                {
                    if (ContinuumEventParser.IsPulseCompletionTarget(label))
                        CompleteInjection(chatId, requestId);
                    return;
                }
            }

        }

        private void HandleSignalReplySettled(ContinuumCommandMessage message)
        {
            var resolvedChatId = _sessionContext.ResolveChatId(message.ChatId);
            if (string.IsNullOrWhiteSpace(resolvedChatId))
                return;

            var requestId = (message.RequestId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(requestId) ||
                !TryGetPendingSignalPulse(resolvedChatId, out var pending) ||
                _operations.CurrentOperationId != pending.OperationId ||
                !requestId.Equals(pending.SignalRequestId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var signalReply = message.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(signalReply) ||
                !SignalPacket.TryParse(signalReply, out var signalSummary))
            {
                HandleSignalFailure(resolvedChatId, requestId);
                return;
            }

            var renderedPulsePacket = PulsePacketComposer.Compose(
                pending.Snapshot,
                pending.DeterministicSections,
                signalSummary);
            if (string.IsNullOrWhiteSpace(renderedPulsePacket) ||
                !TryTakePendingSignalPulse(resolvedChatId, pending.OperationId, requestId, out pending))
            {
                HandleSignalFailure(resolvedChatId, requestId);
                return;
            }

            try
            {
                var token = _operations.GetTokenIfRunning(GuardedOperationKind.Pulse);
                var seed = _quickRefresh.CreatePulseSeed(
                    resolvedChatId,
                    renderedPulsePacket,
                    "PulsePacketComposer",
                    token);
                EnqueueFinalSeed(seed);
            }
            catch (OperationCanceledException)
            {
                FinishRefresh(resolvedChatId, showReadyToast: false);
                ShowOperationCancelled();
            }
            catch
            {
                EnqueueFinalSeed(pending.DeterministicFallbackSeed);
            }
        }

        private void HandleSignalFailure(string? chatId, string? requestId)
        {
            var resolvedChatId = _sessionContext.ResolveChatId(chatId);
            if (string.IsNullOrWhiteSpace(resolvedChatId) ||
                !TryTakePendingSignalPulseForCurrentOperation(resolvedChatId, requestId, out var pending))
            {
                return;
            }

            EnqueueFinalSeed(pending.DeterministicFallbackSeed);
        }

        private bool TryGetPendingSignalPulse(string chatId, out PendingSignalPulse pending)
        {
            lock (_sync)
            {
                if (_pendingSignalPulse == null ||
                    !_pendingSignalPulse.ChatId.Equals(chatId, StringComparison.OrdinalIgnoreCase))
                {
                    pending = null!;
                    return false;
                }

                pending = _pendingSignalPulse;
                return true;
            }
        }

        private bool TryTakePendingSignalPulseForCurrentOperation(
            string chatId,
            string? requestId,
            out PendingSignalPulse pending)
        {
            pending = null!;
            if (!_operations.IsRunning(GuardedOperationKind.Pulse))
                return false;

            var operationId = _operations.CurrentOperationId;
            return operationId > 0 && TryTakePendingSignalPulse(chatId, operationId, requestId, out pending);
        }

        private bool TryTakePendingSignalPulse(
            string chatId,
            long operationId,
            string? requestId,
            out PendingSignalPulse pending)
        {
            lock (_sync)
            {
                if (_pendingSignalPulse == null ||
                    _pendingSignalPulse.OperationId != operationId ||
                    !_pendingSignalPulse.ChatId.Equals(chatId, StringComparison.OrdinalIgnoreCase))
                {
                    pending = null!;
                    return false;
                }

                var expectedRequestId = _pendingSignalPulse.SignalRequestId;
                if (!string.IsNullOrWhiteSpace(requestId) &&
                    !string.IsNullOrWhiteSpace(expectedRequestId) &&
                    !requestId.Equals(expectedRequestId, StringComparison.OrdinalIgnoreCase))
                {
                    pending = null!;
                    return false;
                }

                pending = _pendingSignalPulse;
                _pendingSignalPulse = null;
                CancelTimeout(pending);
                CancelClientInjection(pending.SignalRequestId);
                return true;
            }
        }

        private void StartSignalTimeout(PendingSignalPulse pending)
        {
            _backgroundTasks.Run(
                "Pulse signal timeout",
                async cancellationToken =>
                {
                    await Task.Delay(SignalResponseTimeout, cancellationToken).ConfigureAwait(false);
                    _postToUi(() =>
                    {
                        if (!_operations.IsRunning(GuardedOperationKind.Pulse) ||
                            _operations.CurrentOperationId != pending.OperationId)
                        {
                            return;
                        }

                        if (TryTakePendingSignalPulse(
                                pending.ChatId,
                                pending.OperationId,
                                pending.SignalRequestId,
                                out var timedOutPending))
                        {
                            EnqueueFinalSeed(timedOutPending.DeterministicFallbackSeed);
                        }
                    });
                },
                cancellationToken: pending.TimeoutCancellation.Token);
        }

        private bool RequestCaptureFlushAndArmPulse(string chatId)
        {
            try
            {
                var requestId = Guid.NewGuid().ToString("N");
                var operationId = _operations.CurrentOperationId;
                lock (_sync)
                {
                    _pendingPulseChatId = chatId;
                    _pendingFlushRequestId = requestId;
                }

                _messageSender.Send(new MessageEnvelope
                {
                    Type = WebMessageTypes.Command,
                    Name = WebCommandNames.ContinuumCaptureFlush,
                    ChatId = chatId,
                    Payload = JsonSerializer.SerializeToElement(new
                    {
                        chatId,
                        requestId,
                        reason = "pulse",
                    }),
                });

                _backgroundTasks.Run("Pulse capture flush timeout", async cancellationToken =>
                {
                    await Task.Delay(FlushAckTimeout, cancellationToken).ConfigureAwait(false);

                    var isPending = false;
                    lock (_sync)
                    {
                        if (_pendingPulseChatId == chatId && _pendingFlushRequestId == requestId)
                        {
                            _pendingPulseChatId = null;
                            _pendingFlushRequestId = null;
                            isPending = true;
                        }
                    }

                    if (!isPending)
                        return;

                    _postToUi(() =>
                    {
                        if (!_operations.IsRunning(GuardedOperationKind.Pulse) || _operations.CurrentOperationId != operationId)
                            return;

                        if (_operations.IsCancellationRequested(GuardedOperationKind.Pulse))
                        {
                            FinishRefresh(chatId, showReadyToast: false);
                            ShowOperationCancelled();
                            return;
                        }

                        RunPulseNow(chatId);
                    });
                });

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void RunPulseNow(string chatId)
        {
            try
            {
                if (_operations.IsCancellationRequested(GuardedOperationKind.Pulse))
                {
                    FinishRefresh(chatId, showReadyToast: false);
                    ShowOperationCancelled();
                    return;
                }

                var token = _operations.GetTokenIfRunning(GuardedOperationKind.Pulse);
                RunPulseWithSignalFallback(chatId, token);
            }
            catch (OperationCanceledException)
            {
                FinishRefresh(chatId, showReadyToast: false);
                ShowOperationCancelled();
            }
            catch
            {
                ShowActionUnavailable(chatId, ToastReason.Background);
                FinishRefresh(chatId, showReadyToast: false);
            }
        }

        private void RunPulseWithSignalFallback(string chatId, CancellationToken token)
        {
            var snapshot = _quickRefresh.BuildPulseSnapshot(chatId, token);
            token.ThrowIfCancellationRequested();

            var deterministicSections = _quickRefresh.BuildDeterministicPulseSections(chatId, snapshot, token);
            token.ThrowIfCancellationRequested();

            var deterministicFallbackPacket = _quickRefresh.BuildDeterministicPulsePacket(
                snapshot,
                deterministicSections,
                token);
            var deterministicFallbackSeed = _quickRefresh.CreatePulseSeed(
                chatId,
                deterministicFallbackPacket,
                "PulsePacketComposer",
                token);
            token.ThrowIfCancellationRequested();

            var signalPrompt = (ContinuumAssetLoader.LoadSignalPrompt(chatId) ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(signalPrompt))
            {
                EnqueueFinalSeed(deterministicFallbackSeed);
                return;
            }

            var signalInput = SignalInputBuilder.Build(signalPrompt);
            var operationId = _operations.CurrentOperationId;
            if (string.IsNullOrWhiteSpace(signalInput) ||
                operationId <= 0)
            {
                EnqueueFinalSeed(deterministicFallbackSeed);
                return;
            }

            var signalRequestId = Guid.NewGuid().ToString("N");
            var pending = new PendingSignalPulse
            {
                OperationId = operationId,
                ChatId = chatId,
                SignalRequestId = signalRequestId,
                Snapshot = snapshot,
                DeterministicSections = deterministicSections,
                DeterministicFallbackSeed = deterministicFallbackSeed,
            };

            lock (_sync)
                _pendingSignalPulse = pending;

            _injectQueue.Enqueue(new EssenceInjectController.InjectSeed
            {
                ChatId = chatId,
                Mode = "Signal",
                EssenceText = signalInput,
                OpenNewChat = false,
                AutoSend = true,
                RequestId = signalRequestId,
                CancellationToken = token,
                SourceFileName = "SignalInputBuilder",
                EssenceFileName = "Signal.Prompt.v1.txt",
            });

            StartSignalTimeout(pending);
        }

        private void EnqueueFinalSeed(EssenceInjectController.InjectSeed seed)
        {
            lock (_sync)
            {
                if (!_refreshInFlight || _operations.CurrentOperationId != _refreshOperationId)
                    return;
                seed.RequestId = _injectionRequestId!;
                seed.CancellationToken = _operations.GetTokenIfRunning(GuardedOperationKind.Pulse);
                if (!seed.CancellationToken.IsCancellationRequested)
                    _injectQueue.Enqueue(seed);
            }
        }

        private void StartLifetimeWatch(string chatId)
        {
            var operationId = _refreshOperationId;
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                _operations.GetTokenIfRunning(GuardedOperationKind.Pulse));
            var lifetimeToken = lifetime.Token;
            lock (_sync) _lifetimeCancellation = lifetime;
            _backgroundTasks.Run("Pulse lifetime", async token =>
            {
                // The delegate must run even when cancellation precedes task scheduling.
                using var watchCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken, token);
                var cancelled = false;
                try { await Task.Delay(_operationTimeout, watchCancellation.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { cancelled = true; }
                if (!cancelled)
                    _operations.RequestCancel(GuardedOperationKind.Pulse, operationId);
                _postToUi(() =>
                {
                    lock (_sync)
                    {
                        if (!_refreshInFlight || _refreshOperationId != operationId)
                            return;
                    }
                    FinishRefresh(chatId, showReadyToast: false);
                    if (cancelled) ShowOperationCancelled();
                    else ShowActionUnavailable(chatId, ToastReason.Background);
                });
            });
        }

        private void CancelClientInjection(string? requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId)) return;
            try
            {
                _messageSender.Send(new MessageEnvelope
                {
                    Type = WebMessageTypes.Command,
                    Name = WebCommandNames.ContinuumInjectCancel,
                    Payload = JsonSerializer.SerializeToElement(new { requestId }),
                });
            }
            catch { }
        }

        private void ShowActionUnavailable(string? chatId, ToastReason reason)
        {
            _toasts.TryShow(
                ToastKey.ActionUnavailable,
                chatId: chatId,
                bypassLaunchQuiet: true,
                groupKeyOverride: "pulse",
                replaceGroupOverride: true,
                origin: ToastOrigin.Continuum,
                reason: reason);
        }

        private void ShowOperationInProgress(ToastReason reason)
        {
            _toasts.TryShow(
                ToastKey.OperationInProgress,
                bypassLaunchQuiet: true,
                origin: ToastOrigin.Continuum,
                reason: reason);
        }

        private void ShowOperationCancelled()
        {
            _toasts.TryShowOperationCancelled(
                "pulse",
                ToastOrigin.Continuum,
                ToastReason.Background);
        }

        private static void CancelTimeout(PendingSignalPulse? pending)
        {
            if (pending == null)
                return;

            try
            {
                pending.TimeoutCancellation.Cancel();
            }
            catch
            {
                // Timeout cleanup is best-effort.
            }
            finally
            {
                pending.TimeoutCancellation.Dispose();
            }
        }

        private sealed class PendingSignalPulse
        {
            public long OperationId { get; init; }
            public string ChatId { get; init; } = string.Empty;
            public string SignalRequestId { get; init; } = string.Empty;
            public PulseSnapshot Snapshot { get; init; } = new();
            public DeterministicPulseSections DeterministicSections { get; init; } = new();
            public EssenceInjectController.InjectSeed DeterministicFallbackSeed { get; init; } = new();
            public CancellationTokenSource TimeoutCancellation { get; } = new();
        }

        public void Dispose()
        {
            FinishRefresh(_refreshChatId ?? string.Empty, showReadyToast: false);
        }
    }
}
