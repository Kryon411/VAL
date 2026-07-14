using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using VAL.Continuum.Pipeline;
using VAL.Continuum.Pipeline.Inject;
using VAL.Continuum.Pipeline.QuickRefresh;
using VAL.Continuum.Pipeline.Truth;
using VAL.Contracts;
using VAL.Host;
using VAL.Host.Commands;
using VAL.Host.Services;
using VAL.Host.WebMessaging;

namespace VAL.Continuum
{
    internal sealed class ContinuumHost
    {
        private readonly IWebMessageSender _messageSender;
        private readonly IToastHub _toastHub;
        private readonly ITruthStore _writer;
        private readonly ISessionContext _sessionContext;
        private readonly IBackgroundTaskSupervisor _backgroundTasks;
        private readonly IProcessLauncher _processLauncher;
        private readonly ContinuumArchiveService _archives;
        private readonly ContinuumChronicleWorkflow _chronicle;
        private readonly ContinuumPulseWorkflow _pulse;

        // Best-effort UI context captured from the WebMessageReceived thread (usually UI/Dispatcher).
        private SynchronizationContext? _uiCtx;

        public ContinuumHost(
            IWebMessageSender messageSender,
            IToastHub toastHub,
            ITruthStore writer,
            IQuickRefreshService quickRefreshService,
            IContinuumInjectInbox injectQueue,
            ISessionContext sessionContext,
            OperationCoordinator operationCoordinator,
            IToastLedger toastLedger,
            IBackgroundTaskSupervisor backgroundTasks,
            IProcessLauncher processLauncher,
            ContinuumArchiveService archives)
        {
            _messageSender = messageSender ?? throw new ArgumentNullException(nameof(messageSender));
            _toastHub = toastHub ?? throw new ArgumentNullException(nameof(toastHub));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            ArgumentNullException.ThrowIfNull(quickRefreshService);
            ArgumentNullException.ThrowIfNull(injectQueue);
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            ArgumentNullException.ThrowIfNull(operationCoordinator);
            _backgroundTasks = backgroundTasks ?? throw new ArgumentNullException(nameof(backgroundTasks));
            _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
            _archives = archives ?? throw new ArgumentNullException(nameof(archives));
            _chronicle = new ContinuumChronicleWorkflow(
                _messageSender,
                _toastHub,
                _writer,
                _sessionContext,
                operationCoordinator,
                toastLedger ?? throw new ArgumentNullException(nameof(toastLedger)),
                _archives,
                IsRefreshInFlight,
                MarkChronicleAttemptFinished,
                MarkChronicleCompleted);
            _pulse = new ContinuumPulseWorkflow(
                _messageSender,
                _toastHub,
                quickRefreshService,
                injectQueue,
                _sessionContext,
                operationCoordinator,
                _backgroundTasks,
                _archives,
                () => _chronicle.IsInFlight,
                PostToUi);
        }

        public bool IsMessageSenderWired => _messageSender != null;

        private Action<MessageEnvelope> PostToWebMessage => _messageSender.Send;
        private IToastHub Toasts => _toastHub;
        private ITruthStore Writer => _writer;
        private ISessionContext SessionContext => _sessionContext;

        private readonly object Sync = new object();
        private bool _loggingEnabled = true;

        // Toast intent gating:
        // - Chronicle guidance: shown only after the user has dwelled in the chat for a moment
        //   (prevents spam when bouncing between chats) *and* then interacts with the composer.
        private int _toastAttachToken;
        private string _toastAttachChatId = string.Empty;

        // Chronicle prompt gating should be per chat (not global), otherwise baseline math can bleed across chat switches.
        // Baseline is captured on the first composer interaction after attach for a given chatId.
        private readonly System.Collections.Generic.Dictionary<string, int> _chronicleBaselineTurnsByChat =
            new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);

        // Chronicle prompt should not spam on rapid chat switching. Track last-shown per chat and only re-show after a window.
        private readonly System.Collections.Generic.Dictionary<string, System.DateTime> _chroniclePromptLastShownUtcByChat =
            new System.Collections.Generic.Dictionary<string, System.DateTime>(System.StringComparer.OrdinalIgnoreCase);

        private static readonly System.TimeSpan ChroniclePromptReshowWindow = System.TimeSpan.FromMinutes(30);
        private bool _toastAttachDwellMet;
        private bool _toastAttachChronicleShown;
        private int _toastAttachLastCapturedTurns;

        private static readonly TimeSpan ToastAttachDwellWindow = TimeSpan.FromSeconds(2);


        // Session attach watchdog de-dupe: repeated attach pings may occur while the host is initializing.
        private string _lastSessionAttachHandledChatId = string.Empty;
        private DateTime _lastSessionAttachHandledUtc = DateTime.MinValue;
        private static readonly TimeSpan AttachDedupeWindow = TimeSpan.FromSeconds(3);



        public void HandleCommand(HostCommand cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd.Type))
                return;

            var type = cmd.Type.Trim();

            var msg = ContinuumCommandMessage.From(cmd);

            // Authoritative session context update.
            SessionContext.Observe(type, msg.ChatId);

            // Best-effort capture of UI context from the WebMessageReceived thread.
            _uiCtx ??= SynchronizationContext.Current;

            // Capture flush acknowledgements (Pulse preflight)
            if (type.Equals(WebCommandNames.ContinuumCaptureFlushAck, StringComparison.OrdinalIgnoreCase))
            {
                _pulse.HandleCaptureFlushAck(msg);
                return;
            }

            if (type.Equals(WebCommandNames.ContinuumSessionAttached, StringComparison.OrdinalIgnoreCase) ||
                type.Equals(WebCommandNames.ContinuumSessionAttach, StringComparison.OrdinalIgnoreCase))
            {
                HandleSessionAttach(msg.ChatId);
                return;
            }

            if (type.Equals(WebCommandNames.ContinuumCommandToggleLogging, StringComparison.OrdinalIgnoreCase))
            {
                var reason = ToastReasonParser.Parse(msg.Reason, ToastReason.DockClick);
                HandleToggleLogging(msg.Enabled ?? true, reason);
                return;
            }

            if (type.Equals(WebCommandNames.ContinuumUiComposerInteraction, StringComparison.OrdinalIgnoreCase))
            {
                var reason = ToastReasonParser.Parse(msg.Reason, ToastReason.DockClick);
                HandleChronicleComposerInteraction(msg.ChatId, msg.CapturedTurns ?? 0, reason);
                return;
            }

            if (type.Equals(WebCommandNames.ContinuumTruthAppend, StringComparison.OrdinalIgnoreCase) ||
                type.Equals(WebCommandNames.TruthAppend, StringComparison.OrdinalIgnoreCase))
            {
                bool enabled;
                bool chronicle;
                lock (Sync) { enabled = _loggingEnabled; chronicle = _chronicle.IsInFlight; }
                if (!enabled && !chronicle) return;

                var cid = SessionContext.ResolveChatId(msg.ChatId);
                if (string.IsNullOrWhiteSpace(cid)) return;

                var txt = msg.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(txt)) return;

                var role = ContinuumTruthCaptureParser.ParseRole(msg.Role);

                // Detect Continuum/Pulse-seeded chats from the injected CONTINUUM CONTEXT payload.
                // (This is what makes Chronicle prompts semantically suppressible in Pulse-created chats.)
                if (role == 'U' && ContinuumSeedClassifier.IsContinuumSeed(txt))
                    SessionContext.MarkContinuumSeeded(cid);

                Writer.AppendTruthLine(cid, role, txt);
                return;
            }

            if (type.Equals(WebCommandNames.ContinuumTruth, StringComparison.OrdinalIgnoreCase))
            {
                bool enabled;
                bool chronicle;
                lock (Sync) { enabled = _loggingEnabled; chronicle = _chronicle.IsInFlight; }
                if (!enabled && !chronicle) return;

                var cid = SessionContext.ResolveChatId(msg.ChatId);
                if (string.IsNullOrWhiteSpace(cid)) return;

                if (ContinuumTruthCaptureParser.TryParseLegacyLine(msg.Line, out var role, out var text))
                {
                    // Detect Continuum/Pulse-seeded chats from the injected CONTINUUM CONTEXT payload.
                    // NOTE: client escapes newlines as "\\n", but the marker strings remain intact.
                    if (role == 'U' && ContinuumSeedClassifier.IsContinuumSeed(text))
                        SessionContext.MarkContinuumSeeded(cid);

                    Writer.AppendTruthLine(cid, role, text);
                }

                return;
            }

            if (type.Equals(WebCommandNames.ContinuumCommandPulse, StringComparison.OrdinalIgnoreCase) ||
                type.Equals(WebCommandNames.ContinuumCommandRefreshQuick, StringComparison.OrdinalIgnoreCase))
            {
                var reason = ToastReasonParser.Parse(msg.Reason, ToastReason.DockClick);
                _pulse.Start(msg.ChatId, reason);
                return;
            }

            if (type.Equals(WebCommandNames.ContinuumCommandOpenSessionFolder, StringComparison.OrdinalIgnoreCase))
            {
                HandleOpenSessionFolder(msg.ChatId);
                return;
            }

            if (type.Equals(WebCommandNames.ContinuumCommandChronicleCancel, StringComparison.OrdinalIgnoreCase) ||
                type.Equals(WebCommandNames.ContinuumCommandCancelChronicle, StringComparison.OrdinalIgnoreCase))
            {
                var reason = ToastReasonParser.Parse(msg.Reason, ToastReason.DockClick);
                _chronicle.Cancel(msg.ChatId, reason);
                return;
            }

            if (type.Equals(WebCommandNames.ContinuumCommandChronicleRebuildTruth, StringComparison.OrdinalIgnoreCase) ||
                type.Equals(WebCommandNames.ContinuumCommandChronicle, StringComparison.OrdinalIgnoreCase))
            {
                var reason = ToastReasonParser.Parse(msg.Reason, ToastReason.DockClick);
                _chronicle.Start(msg.ChatId, reason);
                return;
            }

            if (type.Equals(WebCommandNames.ContinuumChronicleProgress, StringComparison.OrdinalIgnoreCase))
            {
                // Toast Catalog v1: no progress toasts.
                return;
            }

            if (type.Equals(WebCommandNames.ContinuumChronicleDone, StringComparison.OrdinalIgnoreCase))
            {
                _chronicle.Complete(msg);
                return;
            }

            if (type.Equals(WebCommandNames.ContinuumEvent, StringComparison.OrdinalIgnoreCase))
            {
                _pulse.HandleEvent(msg);
                return;
            }

            if (type.Equals(WebCommandNames.InjectSuccess, StringComparison.OrdinalIgnoreCase) ||
                type.Equals("refresh.inject.success", StringComparison.OrdinalIgnoreCase))
            {
                _pulse.CompleteInjection(msg.ChatId);
                return;
            }
        }

        // -------------------------
        // Session attach
        // -------------------------
        private void HandleSessionAttach(string? chatId)
        {
            var cid = SessionContext.ResolveChatId(chatId);

            // Determine missing archive state for this attach (used for toast suppression).
            bool isValidChat =
                !string.IsNullOrWhiteSpace(cid) &&
                !cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase);

            bool attachMissingTruthLog = false;
            if (isValidChat)
                attachMissingTruthLog = !_archives.HasTruthLog(cid);

            bool chronicleRunning;
            bool isDuplicateAttach = false;

            var nowUtc = DateTime.UtcNow;

            lock (Sync)
            {
                chronicleRunning = _chronicle.IsInFlight;

                // Dedupe: the client may ping session.attach repeatedly during a short watchdog window.
                if (!string.IsNullOrWhiteSpace(cid) &&
                    cid.Equals(_lastSessionAttachHandledChatId, StringComparison.OrdinalIgnoreCase) &&
                    _lastSessionAttachHandledUtc != DateTime.MinValue &&
                    (nowUtc - _lastSessionAttachHandledUtc) < AttachDedupeWindow)
                {
                    isDuplicateAttach = true;
                }
                else
                {
                    _lastSessionAttachHandledChatId = cid;
                    _lastSessionAttachHandledUtc = nowUtc;

                    // Reset toast intent gates for this attach.
                    _toastAttachToken++;
                    _toastAttachChatId = cid;
                    _toastAttachDwellMet = false;
                    _toastAttachChronicleShown = false;
                    _toastAttachLastCapturedTurns = 0;
                }
            }

            if (!isValidChat)
                return;

            // Ensure per-chat meta exists (defaults Origin to Organic until proven otherwise).
            // Also persist the attach-time archive state for Chronicle prompt gating.
            try
            {
                SessionContext.EnsureInitialized(cid);
                SessionContext.SetMissingTruthLogAtAttach(cid, attachMissingTruthLog);
            }
            catch { }

            // Ack to the client so its attach watchdog can stop deterministically.
            try
            {
                var post = PostToWebMessage;
                if (post != null && !string.IsNullOrWhiteSpace(cid))
                {
                    post(new MessageEnvelope
                    {
                        Type = WebMessageTypes.Event,
                        Name = WebCommandNames.ContinuumSessionAttached,
                        ChatId = cid,
                        Payload = JsonSerializer.SerializeToElement(new { chatId = cid })
                    });
                }
            }
            catch { }

            try
            {
                SendContractsBootstrap(cid);
            }
            catch { }

            // Suppress all archive-related guidance/lifecycle toasts while Chronicle is running.
            if (chronicleRunning) return;

            // Ignore duplicate attach pings for the same chat.
            if (isDuplicateAttach) return;

            // Clear chat-specific toasts when the user switches chats.
            try
            {
                Toasts.DismissGroup("continuum_guidance");
                Toasts.DismissGroup("continuum.lifecycle");
                Toasts.DismissGroup("chronicle");
            }
            catch { }

            // Arm dwell gate for this attach (best-effort; no firing without composer interaction).
            int token;
            string gateCid;
            lock (Sync) { token = _toastAttachToken; gateCid = _toastAttachChatId; }

            _backgroundTasks.Run("Continuum attach dwell", async cancellationToken =>
            {
                await Task.Delay(ToastAttachDwellWindow, cancellationToken).ConfigureAwait(false);
                lock (Sync)
                {
                    if (_toastAttachToken == token &&
                        !string.IsNullOrWhiteSpace(_toastAttachChatId) &&
                        _toastAttachChatId.Equals(gateCid, StringComparison.OrdinalIgnoreCase))
                    {
                        _toastAttachDwellMet = true;
                    }
                }
            });
        }

        private void SendContractsBootstrap(string? chatId)
        {
            var post = PostToWebMessage;
            if (post == null)
                return;

            var payload = WebCommandNames.GetAll();
            post(new MessageEnvelope
            {
                Type = WebMessageTypes.ContractsBootstrap,
                ChatId = chatId,
                Payload = JsonSerializer.SerializeToElement(payload)
            });
        }



        internal void ApplyLoggingSetting(bool enable, bool showToast, ToastReason reason = ToastReason.Background)
        {
            bool showPausedToast = false;
            lock (Sync)
            {
                if (_loggingEnabled && !enable)
                    showPausedToast = true;

                _loggingEnabled = enable;
            }

            // User-invoked: bypass launch quiet period.
            if (showToast && showPausedToast)
            {
                Toasts.TryShow(
                    ToastKey.ContinuumArchivingPaused,
                    bypassLaunchQuiet: true,
                    origin: ToastOrigin.Continuum,
                    reason: reason);
            }
        }

        private void HandleToggleLogging(bool enable, ToastReason reason)
        {
            ApplyLoggingSetting(enable, showToast: true, reason: reason);
        }

        // -------------------------
        // Pulse
        // -------------------------
        private void PostToUi(Action act)
        {
            try
            {
                var ctx = _uiCtx;
                if (ctx != null)
                {
                    ctx.Post(_ => { try { act(); } catch { } }, null);
                    return;
                }
            }
            catch { }

            try { act(); } catch { }
        }

        // -------------------------
        // Chronicle: rebuild Truth.log from the UI (user-invoked recovery tool)
        // -------------------------

        private void HandleChronicleComposerInteraction(string? chatId, int capturedTurns, ToastReason reason)
        {
            try
            {
                var cid = SessionContext.ResolveChatId(chatId);
                if (string.IsNullOrWhiteSpace(cid)) return;
                if (cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase)) return;

                // Suppress while Chronicle is running.
                bool chronicleRunning;
                bool attachMatch;
                bool attachDwellMet;
                bool chronicleAlreadyShown;

                var nowUtc = DateTime.UtcNow;
                var pulseGuidanceSuppressed = _pulse.IsGuidanceSuppressed(nowUtc);

                lock (Sync)
                {
                    chronicleRunning = _chronicle.IsInFlight;

                    attachMatch = !string.IsNullOrWhiteSpace(_toastAttachChatId) &&
                                  _toastAttachChatId.Equals(cid, StringComparison.OrdinalIgnoreCase);

                    if (attachMatch)
                    {
                        _toastAttachLastCapturedTurns = capturedTurns;
                    }

                    attachDwellMet = attachMatch && _toastAttachDwellMet;
                    // Treat the Chronicle prompt as "already shown recently" for this chat to avoid spam on rapid chat switching.
                    bool shownRecently = false;
                    if (_chroniclePromptLastShownUtcByChat.TryGetValue(cid, out var lastShownUtc))
                    {
                        shownRecently = (nowUtc - lastShownUtc) < ChroniclePromptReshowWindow;
                    }

                    chronicleAlreadyShown = !attachMatch || _toastAttachChronicleShown || shownRecently;
                }

                if (chronicleRunning) return;

                // First composer interaction after attach (per chat): capture baseline turns and exit.
                // This prevents lifecycle/guidance toasts from firing on a mere click/focus,
                // and allows gating on a real user↔assistant exchange (turns +2) instead.
                int baselineTurns;
                lock (Sync)
                {
                    if (!_chronicleBaselineTurnsByChat.TryGetValue(cid, out baselineTurns))
                    {
                        _chronicleBaselineTurnsByChat[cid] = capturedTurns;
                        return;
                    }
                }

                bool commitReady = capturedTurns >= (baselineTurns + 2);
                if (!commitReady) return;

                // Chronicle prompt is more intrusive: only consider it after the user has dwelled in the chat.
                if (!attachDwellMet) return;

                // Suppress Chronicle nudges during Pulse/refresh and the post-Pulse suppression window.
                if (pulseGuidanceSuppressed) return;
                if (chronicleAlreadyShown) return;

                // AUTHORITATIVE POLICY: suppress Chronicle prompts in Pulse/Continuum-seeded chats.
                if (_archives.IsContinuumSeededChat(cid)) return;

                // Only if Chronicle has not completed for this chat yet.
                if (_archives.HasChronicleMarker(cid)) return;

                // Only prompt on meaningful chats with real history (avoid tiny shells).
                // capturedTurns is the primary signal; we also keep the last value seen this attach as a fallback.
                int turns = capturedTurns;
                if (turns <= 0)
                {
                    lock (Sync) { turns = Math.Max(turns, _toastAttachLastCapturedTurns); }
                }
                if (!_archives.IsMeaningfulChat(cid, turns)) return;

                bool loggingEnabled;
                lock (Sync) { loggingEnabled = _loggingEnabled; }

                bool hasTruthLog = _archives.HasTruthLog(cid);
                bool hasNonTrivialTruthLog = _archives.HasNonTrivialTruthLog(cid);

                int turnsForCompleteness = turns;
                if (turnsForCompleteness <= 0)
                {
                    lock (Sync) { turnsForCompleteness = Math.Max(turnsForCompleteness, _toastAttachLastCapturedTurns); }
                }

                bool missingArchive = !hasTruthLog;
                bool captureIncomplete = hasTruthLog && turnsForCompleteness >= 4 && !hasNonTrivialTruthLog;

                if (!missingArchive && loggingEnabled && hasNonTrivialTruthLog) return;
                if (!(missingArchive || !loggingEnabled || captureIncomplete)) return;

                string? subtitleOverride = null;
                if (missingArchive)
                {
                    subtitleOverride = "Chronicle can rebuild a local Truth log so future Pulse jumps have the right context.";
                }

                // Show sticky action toast (per attach). We bypass ToastLedger so the prompt can re-appear
                // on a fresh attach if Chronicle still hasn't been run.
                var shown = Toasts.TryShowActions(
                    ToastKey.ChroniclePrompt,
                    new (string Label, Action OnClick)[]
                    {
                        ("Chronicle", () =>
                        {
                            try { _chronicle.Start(cid, ToastReason.DockClick); } catch { }
                        }),
                        ("Not now", () => { })
                    },
                    chatId: cid,
                    bypassLaunchQuiet: true,
                    subtitleOverride: subtitleOverride,
                    origin: ToastOrigin.Continuum,
                    reason: reason
                );

                if (shown)
                {
                    lock (Sync)
                    {
                        if (!string.IsNullOrWhiteSpace(_toastAttachChatId) &&
                            _toastAttachChatId.Equals(cid, StringComparison.OrdinalIgnoreCase))
                        {
                            _toastAttachChronicleShown = true;
                        }

                        _chroniclePromptLastShownUtcByChat[cid] = nowUtc;
                    }
                }
            }
            catch { }
        }

        private bool IsRefreshInFlight()
        {
            return _pulse.IsInFlight;
        }

        private void MarkChronicleAttemptFinished(string chatId)
        {
            lock (Sync)
            {
                if (_toastAttachChatId.Equals(chatId, StringComparison.OrdinalIgnoreCase))
                    _toastAttachChronicleShown = true;
            }
        }

        private void MarkChronicleCompleted(string chatId)
        {
            lock (Sync)
            {
                _chronicleBaselineTurnsByChat.Remove(chatId);
                _chroniclePromptLastShownUtcByChat.Remove(chatId);
            }
        }

        private void MaybeShowChronicleSuggested(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return;
            if (chatId.StartsWith("session-", StringComparison.OrdinalIgnoreCase)) return;

            bool chronicleRunning;
            lock (Sync) { chronicleRunning = _chronicle.IsInFlight; }

            // AUTHORITATIVE POLICY: suppress missing-archive guidance while Chronicle is running.
            if (chronicleRunning) return;

            if (!_archives.HasTruthLog(chatId))
            {
                Toasts.TryShow(
                    ToastKey.ChronicleSuggested,
                    chatId: chatId,
                    origin: ToastOrigin.Continuum,
                    reason: ToastReason.Attach);
            }
        }


        private void HandleOpenSessionFolder(string? chatId)
        {
            var cid = SessionContext.ResolveChatId(chatId);
            if (string.IsNullOrWhiteSpace(cid))
                return;

            try
            {
                var dir = Writer.EnsureChatDir(cid);
                _processLauncher.OpenFolder(dir);
            }
            catch
            {
                Toasts.TryShow(
                    ToastKey.ActionUnavailable,
                    bypassLaunchQuiet: true,
                    origin: ToastOrigin.Continuum,
                    reason: ToastReason.Background);
            }
        }

    }
}
