using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using VAL.Host.WebMessaging;
using System.Threading;
using System.Threading.Tasks;
using VAL.Host;
using VAL.Continuum.Pipeline.QuickRefresh;
using VAL.Continuum.Pipeline.Truth;
using VAL.Continuum.Pipeline.Inject;
using VAL.Continuum.Pipeline;

namespace VAL.Continuum
{
    public static class ContinuumHost
    {
        /// <summary>
        /// Host -> Web post hook (wired by MainWindow after WebView is ready).
        /// ContinuumHost stays decoupled from WebView2 types by using this delegate.
        /// </summary>
        public static Action<MessageEnvelope>? PostToWebMessage { get; set; }

        // Best-effort UI context captured from the WebMessageReceived thread (usually UI/Dispatcher).
        private static SynchronizationContext? _uiCtx;

        // -------------------------
        // Toast Catalog v1 (final)
        // -------------------------
        private const string Toast_ContinuumArchivingPaused =
            "Continuum has been paused and archiving has stopped.";

        private const string Toast_PulseInitiated =
            "A Pulse jump has been initiated. Please stand by.";

        private const string Toast_PulseReady =
            "Your Pulse jump is ready. Please hit Send to finalize and continue.";

        private const string Toast_ChronicleStarted =
            "Chronicle has started. VAL is rebuilding an archive for this chat — please do not send messages until Chronicle is complete.";

        private const string Toast_ChronicleCompleted =
            "Chronicle is complete. This chat is now archived and ready for Pulse jumps.";

        private const string Toast_PreludeAvailable =
            "This chat can be prepared for continuation. If you’d like, Prelude can set things up so a future Pulse jump has the right context.";

        // Prelude prompt (new chat): action toast shown when the user interacts with the blank new-chat composer.
        // NOTE: Title includes "detected a new chat" so ToastManager.ShowActions will treat it as Sticky.
        private const string Toast_PreludePromptTitle =
            "Starting a new chat?";

        private const string Toast_PreludePromptSubtitle =
            "Prelude can set up this chat for continuation. If you\u2019d like, it will insert setup and instructions so a future Pulse jump has the right context.";

        // Chronicle prompt (existing chat without a completed Chronicle archive): timed action toast.
        private const string Toast_ChroniclePromptTitle =
            "Want to save this chat?";

        private const string Toast_ChroniclePromptSubtitle =
            "Chronicle can rebuild a local Truth log so future Pulse jumps have the right context.";



        private const string Toast_NoTruthLogFound =
            "There’s no archive for this chat yet. Running Chronicle will create one so Pulse jumps can work properly.";

        private const string Toast_ChronicleSuggested =
            "VAL has detected you’re continuing in a chat without an archive. Chronicle can rebuild one to help maintain context for Pulse jumps. Please select Chronicle in the Control Centre.";

        private const string Toast_PulseAlreadyRunning =
            "A Pulse jump is already in progress. Please wait a moment for it to finish.";

        private const string Toast_PulseUnavailable =
            "Pulse can’t be used in this chat yet. Preparing the chat with Chronicle will make Pulse jumps available.";

        private const string Toast_ChronicleUnavailable =
            "Chronicle can only be used in an existing chat. Please open the conversation you want to archive and try again.";

        private const string Toast_ActionUnavailable =
            "That action isn’t available right now. Please try again in a moment.";

        private const string Toast_OperationInProgress =
            "An operation is already in progress.";

        private const string Toast_OperationCancelled =
            "Operation cancelled.";

        // Toast groups (used for deterministic replacement/dismiss)
        private const string ToastGroup_Chronicle = "chronicle";

        // Pulse flush handshake (prevents missing tail turns).
        private static string? _pendingPulseChatId;
        private static string? _pendingFlushRequestId;
        private static DateTime _pendingFlushRequestedUtc = DateTime.MinValue;

        private const int FlushAckTimeoutMs = 900;

        private sealed class Msg
        {
            public string? type { get; set; }
            public string? chatId { get; set; }

            public string? requestId { get; set; }
            public string? role { get; set; }
            public string? text { get; set; }

            public string? line { get; set; }

            public string? evt { get; set; }

            // UI signals (best-effort)
            public string? href { get; set; }
            public string? reason { get; set; }

            public string? phase { get; set; }
            public int? percent { get; set; }
            public int? capturedTurns { get; set; }
            public long? ms { get; set; }

            // Chronicle client may send an error string on completion.
            public string? error { get; set; }

            public bool? enabled { get; set; }
        }

        private static readonly object Sync = new object();
        private static bool _refreshInFlight;
        private static DateTime _lastRefreshCompletedUtc = DateTime.MinValue;

        // Chronicle (Truth backfill/rebuild) state
        private static bool _chronicleInFlight;
        private static string? _chronicleRequestId;
        private static DateTime _chronicleStartedUtc = DateTime.MinValue;


        // Chronicle running flag (toast suppression + sequencing only).
        private static bool _chronicleRunning;

        // New chat ("/" route) prelude guidance should show once per entry.
        private static bool _preludeAvailableShownForCurrentNewChat;

        // Prelude prompt: once per new-chat root instance (href-based) to avoid spam on repeated clicks.
        private static string _lastPreludePromptHref = string.Empty;

        // Prelude seeding: if user injects Prelude on the New Chat root, the real /c/<uuid>
        // chatId does not exist yet. Carry a one-shot marker across the next session.attach so we can
        // suppress Chronicle prompts for that newly seeded chat.
        private static bool _pendingPreludeSeedForNextAttach;
        private static DateTime _pendingPreludeSeedUntilUtc = DateTime.MinValue;


        // When Pulse opens a new chat automatically, we do NOT want to show the Prelude guidance toast.
        private static DateTime _suppressPreludeToastUntilUtc = DateTime.MinValue;
        private static readonly TimeSpan PreludeToastSuppressWindow = TimeSpan.FromSeconds(25);

        private static readonly TimeSpan RefreshCooldown = TimeSpan.FromSeconds(10);

        private static bool _loggingEnabled = true;

        // Toast intent gating:
        // - Chronicle guidance: shown only after the user has dwelled in the chat for a moment
        //   (prevents spam when bouncing between chats) *and* then interacts with the composer.
        private static int _toastAttachToken;
        private static string _toastAttachChatId = string.Empty;
        private static DateTime _toastAttachUtc = DateTime.MinValue;

        // Chronicle prompt gating should be per chat (not global), otherwise baseline math can bleed across chat switches.
        // Baseline is captured on the first composer interaction after attach for a given chatId.
        private static readonly System.Collections.Generic.Dictionary<string, int> _chronicleBaselineTurnsByChat =
            new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);

        // Chronicle prompt should not spam on rapid chat switching. Track last-shown per chat and only re-show after a window.
        private static readonly System.Collections.Generic.Dictionary<string, System.DateTime> _chroniclePromptLastShownUtcByChat =
            new System.Collections.Generic.Dictionary<string, System.DateTime>(System.StringComparer.OrdinalIgnoreCase);

        private static readonly System.TimeSpan ChroniclePromptReshowWindow = System.TimeSpan.FromMinutes(30);
        private static bool _toastAttachDwellMet;
        private static bool _toastAttachChronicleShown;
        private static int _toastAttachLastCapturedTurns;

        private static readonly TimeSpan ToastAttachDwellWindow = TimeSpan.FromSeconds(2);


        // Session attach watchdog de-dupe: repeated attach pings may occur while the host is initializing.
        private static string _lastSessionAttachHandledChatId = string.Empty;
        private static DateTime _lastSessionAttachHandledUtc = DateTime.MinValue;
        private static readonly TimeSpan AttachDedupeWindow = TimeSpan.FromSeconds(3);

        

        public static void HandleJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return;

            if (!MessageEnvelope.TryParse(json, out var envelope))
                return;

            var type = envelope.Name?.Trim();
            if (string.IsNullOrWhiteSpace(type))
                return;

            Msg? msg = null;
            if (envelope.Payload.HasValue && envelope.Payload.Value.ValueKind == JsonValueKind.Object)
            {
                try { msg = envelope.Payload.Value.Deserialize<Msg>(); } catch { msg = null; }
            }

            if (msg == null)
                msg = new Msg();

            if (string.IsNullOrWhiteSpace(msg.type))
                msg.type = type;

            if (string.IsNullOrWhiteSpace(msg.chatId) && !string.IsNullOrWhiteSpace(envelope.ChatId))
                msg.chatId = envelope.ChatId;

            // Authoritative session context update.
            SessionContext.Observe(type, msg.chatId);

            // Best-effort capture of UI context from the WebMessageReceived thread.
            _uiCtx ??= SynchronizationContext.Current;

            // Capture flush acknowledgements (Pulse preflight)
            if (type.Equals("continuum.capture.flush_ack", StringComparison.OrdinalIgnoreCase))
            {
                HandleCaptureFlushAck(msg);
                return;
            }

            if (type.Equals("continuum.session.attached", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("continuum.session.attach", StringComparison.OrdinalIgnoreCase))
            {
                HandleSessionAttach(msg.chatId);
                return;
            }

            if (type.Equals("continuum.command.toggle_logging", StringComparison.OrdinalIgnoreCase))
            {
                HandleToggleLogging(msg.enabled ?? true);
                return;
            }

            if (type.Equals("continuum.ui.new_chat", StringComparison.OrdinalIgnoreCase))
            {
                // Suppressed: Prelude guidance is shown via the dedicated new-chat Prelude prompt toast.
                return;
            }

            if (type.Equals("continuum.ui.prelude_prompt", StringComparison.OrdinalIgnoreCase))
            {
                HandlePreludePrompt(msg);
                return;
            }

            if (type.Equals("continuum.ui.composer_interaction", StringComparison.OrdinalIgnoreCase))
            {
                HandleChronicleComposerInteraction(msg.chatId, msg.capturedTurns ?? 0);
                return;
            }


            if (type.Equals("continuum.command.inject_preamble", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("continuum.command.inject_prelude", StringComparison.OrdinalIgnoreCase))
            {
                HandleInjectPrelude(msg.chatId);
                return;
            }

            if (type.Equals("continuum.truth.append", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("truth.append", StringComparison.OrdinalIgnoreCase))
            {
                bool enabled;
                bool chronicle;
                lock (Sync) { enabled = _loggingEnabled; chronicle = _chronicleInFlight; }
                if (!enabled && !chronicle) return;

                var cid = SessionContext.ResolveChatId(msg.chatId);
                if (string.IsNullOrWhiteSpace(cid)) return;

                var txt = msg.text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(txt)) return;

                char role = 'U';
                var r = (msg.role ?? string.Empty).Trim().ToLowerInvariant();
                if (r == "a" || r == "assistant") role = 'A';
                if (r == "u" || r == "user") role = 'U';

                // Detect Continuum/Pulse-seeded chats from the injected CONTINUUM CONTEXT payload.
                // (This is what makes Chronicle prompts semantically suppressible in Pulse-created chats.)
                if (role == 'U' && LooksLikeContinuumSeedText(txt))
                    SessionContext.MarkContinuumSeeded(cid);

                TruthStorage.AppendTruthLine(cid, role, txt);
                return;
            }

            if (type.Equals("continuum.truth", StringComparison.OrdinalIgnoreCase))
            {
                bool enabled;
                bool chronicle;
                lock (Sync) { enabled = _loggingEnabled; chronicle = _chronicleInFlight; }
                if (!enabled && !chronicle) return;

                var cid = SessionContext.ResolveChatId(msg.chatId);
                if (string.IsNullOrWhiteSpace(cid)) return;

                var ln = msg.line ?? string.Empty;
                if (string.IsNullOrWhiteSpace(ln)) return;

                char role = 'U';
                try
                {
                    var parts = ln.Split(']');
                    if (parts.Length >= 3)
                    {
                        var tag = parts[1].Replace("[", "").Trim();
                        if (tag.Equals("A", StringComparison.OrdinalIgnoreCase)) role = 'A';
                        if (tag.Equals("U", StringComparison.OrdinalIgnoreCase)) role = 'U';
                    }
                }
                catch { }

                string text = ln;
                try
                {
                    var last = ln.LastIndexOf("] ", StringComparison.Ordinal);
                    if (last >= 0 && last + 2 < ln.Length)
                        text = ln.Substring(last + 2);
                    else
                    {
                        var third = nthIndexOf(ln, ']', 3);
                        if (third >= 0 && third + 1 < ln.Length)
                            text = ln.Substring(third + 1).TrimStart();
                    }
                }
                catch { }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Detect Continuum/Pulse-seeded chats from the injected CONTINUUM CONTEXT payload.
                    // NOTE: client escapes newlines as "\\n", but the marker strings remain intact.
                    if (role == 'U' && LooksLikeContinuumSeedText(text))
                        SessionContext.MarkContinuumSeeded(cid);

                    TruthStorage.AppendTruthLine(cid, role, text);
                }

                return;
            }

            if (type.Equals(QuickRefreshCommands.CommandPulse, StringComparison.OrdinalIgnoreCase) ||
                type.Equals(QuickRefreshCommands.CommandRefreshQuick, StringComparison.OrdinalIgnoreCase))
            {
                HandlePulse(msg.chatId);
                return;
            }

            if (type.Equals("continuum.command.open_session_folder", StringComparison.OrdinalIgnoreCase))
            {
                HandleOpenSessionFolder(msg.chatId);
                return;
            }

            if (type.Equals("continuum.command.chronicle_cancel", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("continuum.command.cancel_chronicle", StringComparison.OrdinalIgnoreCase))
            {
                HandleChronicleCancel(msg.chatId);
                return;
            }

            if (type.Equals("continuum.command.chronicle_rebuild_truth", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("continuum.command.chronicle", StringComparison.OrdinalIgnoreCase))
            {
                HandleChronicleRebuild(msg.chatId);
                return;
            }

            if (type.Equals("continuum.chronicle.progress", StringComparison.OrdinalIgnoreCase))
            {
                // Toast Catalog v1: no progress toasts.
                return;
            }

            if (type.Equals("continuum.chronicle.done", StringComparison.OrdinalIgnoreCase))
            {
                HandleChronicleDone(msg);
                return;
            }

            if (type.Equals("inject.success", StringComparison.OrdinalIgnoreCase) ||
                (type.Equals("continuum.event", StringComparison.OrdinalIgnoreCase) && (msg.evt ?? "").StartsWith("refresh.inject.success", StringComparison.OrdinalIgnoreCase)))
            {
                EndRefresh(SessionContext.ResolveChatId(msg.chatId));
                return;
            }
        }

        // -------------------------
        // Session attach
        // -------------------------
        private static void HandleSessionAttach(string? chatId)
        {
            var cid = SessionContext.ResolveChatId(chatId);

            // Determine missing archive state for this attach (used for toast suppression).
            bool isValidChat =
                !string.IsNullOrWhiteSpace(cid) &&
                !cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase);

            bool attachMissingTruthLog = false;
            if (isValidChat)
                attachMissingTruthLog = !HasTruthLog(cid);

            bool chronicleRunning;
            bool isDuplicateAttach = false;

            var nowUtc = DateTime.UtcNow;

            lock (Sync)
            {
                // Leaving the new-chat route means the Prelude toast can be shown again next time.
                if (isValidChat)
                    _preludeAvailableShownForCurrentNewChat = false;

                if (isValidChat)
                    _lastPreludePromptHref = string.Empty;

                chronicleRunning = _chronicleRunning;

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

                    // If Prelude was injected on the New Chat root (session-*), we won't see a marker in Truth.log
                    // until after the first send. Carry the seed across the first real attach so Chronicle doesn't
                    // prompt unnecessarily in freshly seeded chats.
                    if (isValidChat &&
                        _pendingPreludeSeedForNextAttach &&
                        _pendingPreludeSeedUntilUtc != DateTime.MinValue &&
                        nowUtc <= _pendingPreludeSeedUntilUtc)
                    {
                        SessionContext.MarkContinuumSeeded(cid);
                        _pendingPreludeSeedForNextAttach = false;
                        _pendingPreludeSeedUntilUtc = DateTime.MinValue;
                    }
                    _toastAttachChatId = cid;
                    _toastAttachUtc = nowUtc;
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
                        Type = "event",
                        Name = "continuum.session.attached",
                        ChatId = cid,
                        Payload = JsonSerializer.SerializeToElement(new { chatId = cid })
                    });
                }
            }
            catch { }

            // Suppress all archive-related guidance/lifecycle toasts while Chronicle is running.
            if (chronicleRunning) return;

            // Ignore duplicate attach pings for the same chat.
            if (isDuplicateAttach) return;

            // Clear chat-specific toasts when the user switches chats.
            try
            {
                ToastManager.DismissGroup("continuum_guidance");
                ToastManager.DismissGroup("continuum.lifecycle");
                ToastManager.DismissGroup(ToastGroup_Chronicle);
            }
            catch { }

            // Arm dwell gate for this attach (best-effort; no firing without composer interaction).
            int token;
            string gateCid;
            lock (Sync) { token = _toastAttachToken; gateCid = _toastAttachChatId; }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(ToastAttachDwellWindow).ConfigureAwait(false);
                    lock (Sync)
                    {
                        if (_toastAttachToken == token &&
                            !string.IsNullOrWhiteSpace(_toastAttachChatId) &&
                            _toastAttachChatId.Equals(gateCid, StringComparison.OrdinalIgnoreCase))
                        {
                            _toastAttachDwellMet = true;
                        }
                    }
                }
                catch { }
            });
        }



        private static void HandleToggleLogging(bool enable)
        {
            bool showPausedToast = false;
            lock (Sync)
            {
                if (_loggingEnabled && !enable)
                    showPausedToast = true;

                _loggingEnabled = enable;
            }

            // User-invoked: bypass launch quiet period.
            if (showPausedToast)
            {
                ToastHub.TryShow(ToastKey.ContinuumArchivingPaused, bypassLaunchQuiet: true);
            }
        }

        
        private static void HandlePreludePrompt(Msg msg)
        {
            try
            {
                // Non-blocking, best-effort: only show on blank/new chat root.
                // Client already gates heavily, but we still keep a safe host guard.
                var href = (msg.href ?? string.Empty).Trim();

                // If we don't have an href, we can't do "once per new-chat instance" gating safely.
                if (string.IsNullOrWhiteSpace(href))
                    return;

                lock (Sync)
                {
                    if (string.Equals(_lastPreludePromptHref, href, StringComparison.Ordinal))
                        return;

                    // Don't nudge during (or right after) a Pulse cycle.
                    var now = DateTime.UtcNow;
                    if (_refreshInFlight) return;
                    if (_suppressPreludeToastUntilUtc != DateTime.MinValue && now < _suppressPreludeToastUntilUtc) return;

                    _lastPreludePromptHref = href;
                }

                // New-chat route has no session.attach, so clear any chat-specific toasts here.
                try
                {
                    ToastManager.DismissGroup("continuum_guidance");
                    ToastManager.DismissGroup("continuum.lifecycle");
                    ToastManager.DismissGroup(ToastGroup_Chronicle);
                }
                catch { }

                // Action toast: Prelude inject (host-side) or dismiss.
                ToastHub.TryShowActions(
                    ToastKey.PreludePrompt,
                    new (string Label, Action OnClick)[]
                    {
                        ("Prelude", () =>
                        {
                            try { HandleInjectPrelude(null); } catch { }
                        }),
                        ("Dismiss", () => { })
                    },
                    bypassLaunchQuiet: true
                );
            }
            catch
            {
                // Never let UI prompting break the host.
            }
        }

        private static void MaybeShowPreludeAvailableToast()
        {
            // Passive guidance toast -> honor launch quiet period.
            if (ToastManager.IsLaunchQuietPeriodActive)
                return;

            lock (Sync)
            {
                var now = DateTime.UtcNow;

                // Avoid nudging during (or right after) a Pulse cycle.
                if (_refreshInFlight) return;
                if (_suppressPreludeToastUntilUtc != DateTime.MinValue && now < _suppressPreludeToastUntilUtc) return;

                if (_preludeAvailableShownForCurrentNewChat)
                    return;

                _preludeAvailableShownForCurrentNewChat = true;
            }

            ToastHub.TryShow(ToastKey.PreludeAvailable);
        }

        private static void HandleInjectPrelude(string? chatId)
        {
            // Manual Prelude injection: drop Context.Prelude.txt into the current composer (no autosend).
            var cid = SessionContext.ResolveChatId(chatId);

            // On New Chat root there is no /c/<uuid> yet; Dock will pass a non-empty session-<...> id.
            if (string.IsNullOrWhiteSpace(cid))
                cid = "session-" + DateTime.UtcNow.Ticks;

            var prelude = ContinuumPreamble.LoadPrelude(cid);
            if (string.IsNullOrWhiteSpace(prelude))
            {
                ToastHub.TryShow(ToastKey.ActionUnavailable, bypassLaunchQuiet: true);
                return;
            }

            var seed = new EssenceInjectController.InjectSeed
            {
                ChatId = cid,
                Mode = "Prelude",
                EssenceText = prelude.Trim(),
                OpenNewChat = false,
                SourceFileName = "Prelude",
                EssenceFileName = "Context.Prelude.txt"
            };

            EssenceInjectQueue.Enqueue(seed);

            // Mark this as a seeded chat for Chronicle prompt suppression.
            // If we're on the New Chat root, cid will be session-<...>; carry a one-shot flag until the
            // next real session.attach provides the /c/<uuid> chatId.
            bool isValidChat =
                !string.IsNullOrWhiteSpace(cid) &&
                !cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase);

            lock (Sync)
            {
                if (isValidChat)
                {
                    SessionContext.MarkContinuumSeeded(cid);
                }
                else
                {
                    _pendingPreludeSeedForNextAttach = true;
                    _pendingPreludeSeedUntilUtc = DateTime.UtcNow.AddMinutes(5);
                }
            }
        }

        // -------------------------
        // Pulse
        // -------------------------
        private static void ToastPulse(ToastKey key, string? chatId)
        {
            // Pulse toasts are grouped and replaced via ToastHub defaults.
            ToastHub.TryShow(key, chatId: chatId, bypassLaunchQuiet: true);
        }

        private static void ToastPulseActionUnavailable(string? chatId)
        {
            // ActionUnavailable is shared across the app; for Pulse we keep it in the "pulse" group.
            ToastHub.TryShow(
                ToastKey.ActionUnavailable,
                chatId: chatId,
                bypassLaunchQuiet: true,
                groupKeyOverride: "pulse",
                replaceGroupOverride: true);
        }

        private static void ToastOperationInProgress()
        {
            ToastHub.TryShow(ToastKey.OperationInProgress, bypassLaunchQuiet: true);
        }

        private static void ToastOperationCancelled(string groupKey)
        {
            ToastHub.TryShowOperationCancelled(groupKey);
        }

        private static bool HasTruthLog(string chatId)
        {
            try
            {
                var truthPath = TruthStorage.GetTruthPath(chatId);
                return File.Exists(truthPath) && new FileInfo(truthPath).Length > 4;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasChronicleMarker(string chatId)
        {
            try
            {
                var dir = TruthStorage.EnsureChatDir(chatId);
                var marker = Path.Combine(dir, "Chronicle.complete.flag");
                return File.Exists(marker);
            }
            catch
            {
                return false;
            }
        }

        private static void WriteChronicleMarker(string chatId)
        {
            try
            {
                var dir = TruthStorage.EnsureChatDir(chatId);
                var marker = Path.Combine(dir, "Chronicle.complete.flag");
                if (!File.Exists(marker))
                    File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
            }
            catch { }
        }

        private static bool IsMeaningfulChatForChronicle(string chatId, int capturedTurns)
        {
            // Primary signal (preferred): the client reports how many turns are rendered.
            // This is the correct way to distinguish an older chat with history from a fresh shell.
            if (capturedTurns > 0)
                return capturedTurns >= 4;

            // Fallback (weak): truth.log size. Only used if the client didn't provide a turn count.
            try
            {
                var truthPath = TruthStorage.GetTruthPath(chatId);
                if (!File.Exists(truthPath)) return false;
                return new FileInfo(truthPath).Length >= 2048;
            }
            catch
            {
                return false;
            }
        }

        private static bool LooksLikeContinuumSeedText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Keep this intentionally simple and robust.
            // We only want to classify chats that clearly contain the ESSENCE-M handoff payload.
            bool hasContext =
                text.IndexOf("CONTEXT BLOCK — READ ONLY", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("ESSENCE-M SNAPSHOT (AUTHORITATIVE)", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("ESSENCE\u2011M SNAPSHOT (AUTHORITATIVE)", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("WHERE WE LEFT OFF — LAST COMPLETE EXCHANGE (AUTHORITATIVE)", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("CONTEXT FILLER (REFERENCE ONLY — DO NOT ADVANCE FROM HERE)", StringComparison.OrdinalIgnoreCase) >= 0;

            bool hasAuthoritativeSeed =
                text.IndexOf("ESSENCE-M SNAPSHOT (AUTHORITATIVE)", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("ESSENCE\u2011M SNAPSHOT (AUTHORITATIVE)", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("WHERE WE LEFT OFF — LAST COMPLETE EXCHANGE (AUTHORITATIVE)", StringComparison.OrdinalIgnoreCase) >= 0;

            return hasAuthoritativeSeed || hasContext;
        }


        private static void HandlePulse(string? chatId)
        {
            var cid = SessionContext.ResolveChatId(chatId);

            // Brand-new chat / invalid context: Pulse unavailable.
            if (string.IsNullOrWhiteSpace(cid) || cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase))
            {
                ToastPulse(ToastKey.PulseUnavailable, cid);
                return;
            }

            // Single-flight guard: only one long-running operation at a time.
            if (OperationCoordinator.IsBusy)
            {
                ToastOperationInProgress();
                return;
            }

            // Missing archive: reactive guidance (once per chat).
            if (!HasTruthLog(cid))
            {
                bool chronicleRunning;
                lock (Sync) { chronicleRunning = _chronicleRunning; }

                // AUTHORITATIVE POLICY: while Chronicle is running, suppress missing-archive guidance.
                if (!chronicleRunning)
                {
                    // ToastHub applies the once-per-chat ledger gate for this nudge.
                    ToastPulse(ToastKey.PulseNoTruthLogFound, cid);
                }

                return;
            }

            if (!OperationCoordinator.TryBegin(GuardedOperationKind.Pulse, out _))
            {
                ToastOperationInProgress();
                return;
            }

            if (!TryBeginRefresh(cid))
            {
                OperationCoordinator.End(GuardedOperationKind.Pulse);
                return;
            }

            ToastPulse(ToastKey.PulseInitiated, cid);

            // Preflight: ask the client to flush any pending captures before we read Truth.log.
            // This prevents the common "last assistant message missing" tail failure.
            if (!RequestCaptureFlushAndArmPulse(cid))
            {
                // No bridge / cannot flush; proceed immediately as best-effort.
                RunPulseNow(cid, "no-flush");
            }
        }

        private static bool TryBeginRefresh(string chatId)
        {
            lock (Sync)
            {
                if (!SessionContext.IsSessionAttached)
                {
                    ToastPulseActionUnavailable(chatId);
                    return false;
                }

                if (_refreshInFlight)
                {
                    ToastPulse(ToastKey.PulseAlreadyRunning, chatId);
                    return false;
                }

                if (_lastRefreshCompletedUtc != DateTime.MinValue)
                {
                    var delta = DateTime.UtcNow - _lastRefreshCompletedUtc;
                    if (delta < RefreshCooldown)
                    {
                        ToastPulseActionUnavailable(chatId);
                        return false;
                    }
                }

                _refreshInFlight = true;

                // Suppress Prelude guidance during this refresh (Pulse will navigate to a new chat).
                _suppressPreludeToastUntilUtc = DateTime.UtcNow + PreludeToastSuppressWindow;
                return true;
            }
        }

        private static void FinishRefresh(string chatId, bool showReadyToast)
        {
            bool endedRefresh = false;

            lock (Sync)
            {
                if (_refreshInFlight)
                {
                    _refreshInFlight = false;
                    _lastRefreshCompletedUtc = DateTime.UtcNow;
                    endedRefresh = true;
                }
            }

            if (endedRefresh)
            {
                OperationCoordinator.End(GuardedOperationKind.Pulse);
            }

            if (endedRefresh && showReadyToast && !string.IsNullOrWhiteSpace(chatId))
            {
                ToastPulse(ToastKey.PulseReady, chatId);
            }
        }

        private static void EndRefresh(string chatId)
        {
            // JS emits "inject.success" for any injection (Pulse, Prelude, etc.).
            // Only show Pulse completion if a refresh was actually in-flight.
            FinishRefresh(chatId, showReadyToast: true);
        }

        // -------------------------
        // Pulse preflight: capture flush handshake
        // -------------------------
        private static bool RequestCaptureFlushAndArmPulse(string chatId)
        {
            try
            {
                var post = PostToWebMessage;
                if (post == null) return false;

                var reqId = Guid.NewGuid().ToString("N");

                lock (Sync)
                {
                    _pendingPulseChatId = chatId;
                    _pendingFlushRequestId = reqId;
                    _pendingFlushRequestedUtc = DateTime.UtcNow;
                }

                post(new MessageEnvelope
                {
                    Type = "command",
                    Name = "continuum.capture.flush",
                    ChatId = chatId,
                    Payload = JsonSerializer.SerializeToElement(new
                    {
                        chatId = chatId,
                        requestId = reqId,
                        reason = "pulse"
                    })
                });

                // Fallback: if the client never ACKs, proceed after a short timeout.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(FlushAckTimeoutMs).ConfigureAwait(false);

                        string? cid;
                        string? rid;
                        lock (Sync)
                        {
                            cid = _pendingPulseChatId;
                            rid = _pendingFlushRequestId;
                        }

                        if (cid == chatId && rid == reqId)
                        {
                            // Still pending -> timed out. Clear pending and proceed.
                            lock (Sync)
                            {
                                if (_pendingPulseChatId == chatId && _pendingFlushRequestId == reqId)
                                {
                                    _pendingPulseChatId = null;
                                    _pendingFlushRequestId = null;
                                }
                            }

                            PostToUi(() =>
                            {
                                if (!OperationCoordinator.IsRunning(GuardedOperationKind.Pulse))
                                    return;

                                if (OperationCoordinator.IsCancellationRequested(GuardedOperationKind.Pulse))
                                {
                                    FinishRefresh(chatId, showReadyToast: false);
                                    ToastOperationCancelled("pulse");
                                    return;
                                }

                                RunPulseNow(chatId, "flush-timeout");
                            });
                        }
                    }
                    catch { }
                });

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void HandleCaptureFlushAck(Msg msg)
        {
            try
            {
                var rid = (msg.requestId ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(rid)) return;

                string? cid;
                string? pendingRid;

                lock (Sync)
                {
                    cid = SessionContext.ResolveChatId(msg.chatId);
                    pendingRid = _pendingFlushRequestId;
                }

                if (string.IsNullOrWhiteSpace(cid)) return;

                // Only honor ACK for the currently armed pulse.
                if (!rid.Equals(pendingRid, StringComparison.OrdinalIgnoreCase))
                    return;

                lock (Sync)
                {
                    _pendingPulseChatId = null;
                    _pendingFlushRequestId = null;
                }

                if (!OperationCoordinator.IsRunning(GuardedOperationKind.Pulse))
                    return;

                if (OperationCoordinator.IsCancellationRequested(GuardedOperationKind.Pulse))
                {
                    FinishRefresh(cid, showReadyToast: false);
                    ToastOperationCancelled("pulse");
                    return;
                }

                RunPulseNow(cid, "flush-ack");
            }
            catch { }
        }

        private static void RunPulseNow(string chatId, string reasonTag)
        {
            try
            {
                if (OperationCoordinator.IsCancellationRequested(GuardedOperationKind.Pulse))
                {
                    FinishRefresh(chatId, showReadyToast: false);
                    ToastOperationCancelled("pulse");
                    return;
                }

                var token = OperationCoordinator.GetTokenIfRunning(GuardedOperationKind.Pulse);
                QuickRefreshEntry.Run(chatId, token);
            }
            catch (OperationCanceledException)
            {
                FinishRefresh(chatId, showReadyToast: false);
                ToastOperationCancelled("pulse");
            }
            catch
            {
                // Catalog v1: no detailed exception toasts.
                ToastPulseActionUnavailable(chatId);

                // Clear refresh state (no "ready" toast on failure).
                FinishRefresh(chatId, showReadyToast: false);
            }
        }

        private static void PostToUi(Action act)
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
                
        private static void HandleChronicleComposerInteraction(string? chatId, int capturedTurns)
        {
            try
            {
                var cid = SessionContext.ResolveChatId(chatId);
                if (string.IsNullOrWhiteSpace(cid)) return;
                if (cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase)) return;

                // Suppress while Chronicle is running.
                bool chronicleRunning;
                bool refreshInFlight;
                DateTime suppressPreludeUntil;
                bool attachMatch;
                bool attachDwellMet;
                bool chronicleAlreadyShown;

                var nowUtc = DateTime.UtcNow;

                lock (Sync)
                {
                    chronicleRunning = _chronicleRunning;
                    refreshInFlight = _refreshInFlight;
                    suppressPreludeUntil = _suppressPreludeToastUntilUtc;

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
                if (refreshInFlight) return;
                if (suppressPreludeUntil != DateTime.MinValue && nowUtc < suppressPreludeUntil) return;
                if (chronicleAlreadyShown) return;

                // AUTHORITATIVE POLICY: suppress Chronicle prompts in Pulse/Continuum-seeded chats.
                if (IsContinuumSeededChat(cid)) return;

                // Only if Chronicle has not completed for this chat yet.
                if (HasChronicleMarker(cid)) return;

                // Only prompt on meaningful chats with real history (avoid tiny shells).
                // capturedTurns is the primary signal; we also keep the last value seen this attach as a fallback.
                int turns = capturedTurns;
                if (turns <= 0)
                {
                    lock (Sync) { turns = Math.Max(turns, _toastAttachLastCapturedTurns); }
                }
                if (!IsMeaningfulChatForChronicle(cid, turns)) return;

                // Show sticky action toast (per attach). We bypass ToastLedger so the prompt can re-appear
                // on a fresh attach if Chronicle still hasn't been run.
                var shown = ToastHub.TryShowActions(
                    ToastKey.ChroniclePrompt,
                    new (string Label, Action OnClick)[]
                    {
                        ("Chronicle", () =>
                        {
                            try { HandleChronicleRebuild(cid); } catch { }
                        }),
                        ("Not now", () => { })
                    },
                    chatId: cid,
                    bypassLaunchQuiet: true
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

        private static bool IsContinuumSeededChat(string chatId)
        {
            try
            {
                if (SessionContext.GetOrigin(chatId) == ChatOrigin.ContinuumSeeded)
                    return true;
            }
            catch { }

            // Fallback: scan the current Truth.log head for the Continuum injection markers.
            // This protects against any rare cases where the seed classification wasn't recorded
            // (e.g., if the host missed the append event in an edge case).
            try
            {
                var truthPath = TruthStorage.GetTruthPath(chatId);
                if (!File.Exists(truthPath)) return false;

                using var fs = new FileStream(truthPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var max = (int)Math.Min(32768, fs.Length);
                if (max <= 0) return false;

                var buf = new byte[max];
                var read = fs.Read(buf, 0, max);
                if (read <= 0) return false;

                var head = System.Text.Encoding.UTF8.GetString(buf, 0, read);
                return LooksLikeContinuumSeedText(head);
            }
            catch
            {
                return false;
            }
        }

private static void MaybeShowChronicleSuggested(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return;
            if (chatId.StartsWith("session-", StringComparison.OrdinalIgnoreCase)) return;

            bool chronicleRunning;
            lock (Sync) { chronicleRunning = _chronicleRunning; }

            // AUTHORITATIVE POLICY: suppress missing-archive guidance while Chronicle is running.
            if (chronicleRunning) return;

            try
            {
                var truthPath = TruthStorage.GetTruthPath(chatId);
                if (!File.Exists(truthPath) || new FileInfo(truthPath).Length <= 4)
                {
                    ToastHub.TryShow(ToastKey.ChronicleSuggested, chatId: chatId);
                }
            }
            catch
            {
                // ignore
            }
        }


        private static void HandleChronicleCancel(string? chatId)
        {
            try
            {
                if (!OperationCoordinator.IsRunning(GuardedOperationKind.Chronicle))
                    return;

                OperationCoordinator.RequestCancel();

                // Ask the client to stop its scroll+scan loop.
                var post = PostToWebMessage;
                if (post == null) return;

                var cid = SessionContext.ResolveChatId(chatId);
                if (string.IsNullOrWhiteSpace(cid)) return;

                string rid;
                lock (Sync) { rid = _chronicleRequestId ?? string.Empty; }

                post(new MessageEnvelope
                {
                    Type = "command",
                    Name = "continuum.chronicle.cancel",
                    ChatId = cid,
                    Payload = JsonSerializer.SerializeToElement(new
                    {
                        chatId = cid,
                        requestId = rid
                    })
                });
            }
            catch { }
        }


        private static void HandleChronicleRebuild(string? chatId)
        {
            var cid = SessionContext.ResolveChatId(chatId);
            if (string.IsNullOrWhiteSpace(cid) || cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase))
            {
                ToastHub.TryShow(ToastKey.ChronicleUnavailable, chatId: cid, bypassLaunchQuiet: true);
                return;
            }

            // Single-flight guard: only one long-running operation at a time.
            if (OperationCoordinator.IsBusy)
            {
                ToastOperationInProgress();
                return;
            }

            if (!OperationCoordinator.TryBegin(GuardedOperationKind.Chronicle, out var chronicleToken))
            {
                ToastOperationInProgress();
                return;
            }

            lock (Sync)
            {
                if (!SessionContext.IsSessionAttached)
                {
                    ToastHub.TryShow(ToastKey.ActionUnavailable, bypassLaunchQuiet: true);
                    OperationCoordinator.End(GuardedOperationKind.Chronicle);
                    return;
                }
                if (_refreshInFlight)
                {
                    ToastHub.TryShow(ToastKey.ActionUnavailable, bypassLaunchQuiet: true);
                    OperationCoordinator.End(GuardedOperationKind.Chronicle);
                    return;
                }
                if (_chronicleInFlight)
                {
                    ToastHub.TryShow(ToastKey.ActionUnavailable, bypassLaunchQuiet: true);
                    OperationCoordinator.End(GuardedOperationKind.Chronicle);
                    return;
                }

                _chronicleInFlight = true;
                _chronicleRunning = true;
                _chronicleRequestId = Guid.NewGuid().ToString("N");
                _chronicleStartedUtc = DateTime.UtcNow;
            }

            // Prepare an atomic Truth.log rebuild. Existing Truth.log remains unchanged until commit.
            string backupPath = string.Empty;
            try
            {
                if (!TruthStorage.TryBeginTruthRebuild(cid, chronicleToken, backupExisting: true, out backupPath, out _))
                {
                    lock (Sync)
                    {
                        _chronicleInFlight = false;
                        _chronicleRunning = false;
                        _chronicleRequestId = null;
                    }

                    OperationCoordinator.End(GuardedOperationKind.Chronicle);
                    ToastHub.TryShow(ToastKey.ActionUnavailable, bypassLaunchQuiet: true);
                    return;
                }

                // Best-effort cleanup of derived artifacts (so the session folder reflects the new rebuild)
                TryDeleteDerivedArtifacts(cid);
                TryAppendChronicleAudit(cid, $"Chronicle STARTED  | Utc={DateTime.UtcNow:o} | Backup={backupPath}");
            }
            catch
            {
                TruthStorage.AbortTruthRebuild(cid);
                lock (Sync)
                {
                    _chronicleInFlight = false;
                    _chronicleRunning = false;
                    _chronicleRequestId = null;
                }

                OperationCoordinator.End(GuardedOperationKind.Chronicle);
                ToastHub.TryShow(ToastKey.ActionUnavailable, bypassLaunchQuiet: true);
                return;
            }

            // Ask the client to run a deterministic scroll+scan capture from top->bottom.
            try
            {
                var post = PostToWebMessage;
                if (post == null)
                {
                    // Ensure any sticky Chronicle toast is dismissed on failure.
                    ToastHub.TryShow(
                        ToastKey.ActionUnavailable,
                        bypassLaunchQuiet: true,
                        groupKeyOverride: ToastGroup_Chronicle,
                        replaceGroupOverride: true,
                        bypassBurstDedupeOverride: true);
                    lock (Sync) { _chronicleInFlight = false; _chronicleRunning = false; _chronicleRequestId = null; }
                    try { TruthStorage.AbortTruthRebuild(cid); } catch { }
                    OperationCoordinator.End(GuardedOperationKind.Chronicle);
                    return;
                }

                string rid;
                lock (Sync) { rid = _chronicleRequestId ?? Guid.NewGuid().ToString("N"); }

                post(new MessageEnvelope
                {
                    Type = "command",
                    Name = "continuum.chronicle.start",
                    ChatId = cid,
                    Payload = JsonSerializer.SerializeToElement(new
                    {
                        chatId = cid,
                        requestId = rid,
                        mode = "full"
                    })
                });
                // Sticky instruction toast: remains visible until Chronicle completes (then replaced).
                ToastHub.TryShow(ToastKey.ChronicleStarted, chatId: cid, bypassLaunchQuiet: true);
            }
            catch
            {
                lock (Sync) { _chronicleInFlight = false; _chronicleRunning = false; _chronicleRequestId = null; }
                try { TruthStorage.AbortTruthRebuild(cid); } catch { }
                OperationCoordinator.End(GuardedOperationKind.Chronicle);
                // Ensure any sticky Chronicle toast is dismissed on failure.
                ToastHub.TryShow(
                    ToastKey.ActionUnavailable,
                    bypassLaunchQuiet: true,
                    groupKeyOverride: ToastGroup_Chronicle,
                    replaceGroupOverride: true,
                    bypassBurstDedupeOverride: true);
            }
        }

        private static void HandleChronicleDone(Msg msg)
        {
            try
            {
                var cid = SessionContext.ResolveChatId(msg.chatId);
                if (string.IsNullOrWhiteSpace(cid)) return;

                string? rid;
                bool active;
                DateTime startedUtc;

                lock (Sync)
                {
                    rid = _chronicleRequestId;
                    active = _chronicleInFlight;
                    startedUtc = _chronicleStartedUtc;
                }

                if (!active) return;

                if (!string.IsNullOrWhiteSpace(msg.requestId) && !string.IsNullOrWhiteSpace(rid) &&
                    !msg.requestId.Equals(rid, StringComparison.OrdinalIgnoreCase))
                    return;

                lock (Sync)
                {
                    _chronicleInFlight = false;
                    _chronicleRunning = false;
                    _chronicleRequestId = null;

                    // Chronicle rebuild produces a provenance-complete archive for this chat.
                    // Treat the attach-level toasts as satisfied if this is the currently viewed chat.
                    if (!string.IsNullOrWhiteSpace(_toastAttachChatId) &&
                        _toastAttachChatId.Equals(cid, StringComparison.OrdinalIgnoreCase))
                    {
                        _toastAttachChronicleShown = true;
                    }
                }

                // Ensure the "Chronicle suggested" guidance doesn't appear again for this chat.
                try { ToastLedger.TryMarkShown(cid, "guidance.chronicle_suggested"); } catch { }

                var captured = msg.capturedTurns ?? 0;
                var ms = msg.ms ?? (long)Math.Max(0, (DateTime.UtcNow - startedUtc).TotalMilliseconds);

                // If the client reported an error/abort, dismiss the sticky warning toast and surface a
                // catalog-approved fallback (no new wording).
                if (OperationCoordinator.IsCancellationRequested(GuardedOperationKind.Chronicle))
                {
                    try { TruthStorage.AbortTruthRebuild(cid); } catch { }

                    ToastOperationCancelled(ToastGroup_Chronicle);
                    TryAppendChronicleAudit(cid, $"Chronicle CANCELLED | Utc={DateTime.UtcNow:o} | Captured={captured} | Ms={ms}");
                    OperationCoordinator.End(GuardedOperationKind.Chronicle);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(msg.error))
                {
                    try { TruthStorage.AbortTruthRebuild(cid); } catch { }

                    ToastHub.TryShow(
                        ToastKey.ActionUnavailable,
                        bypassLaunchQuiet: true,
                        groupKeyOverride: ToastGroup_Chronicle,
                        replaceGroupOverride: true,
                        bypassBurstDedupeOverride: true);

                    TryAppendChronicleAudit(cid, $"Chronicle FAILED    | Utc={DateTime.UtcNow:o} | Error={msg.error} | Captured={captured} | Ms={ms}");
                    OperationCoordinator.End(GuardedOperationKind.Chronicle);
                    return;
                }

                if (!TruthStorage.TryCommitTruthRebuild(cid))
                {
                    ToastHub.TryShow(
                        ToastKey.ActionUnavailable,
                        bypassLaunchQuiet: true,
                        groupKeyOverride: ToastGroup_Chronicle,
                        replaceGroupOverride: true,
                        bypassBurstDedupeOverride: true);

                    TryAppendChronicleAudit(cid, $"Chronicle FAILED    | Utc={DateTime.UtcNow:o} | Error=commit_failed | Captured={captured} | Ms={ms}");
                    OperationCoordinator.End(GuardedOperationKind.Chronicle);
                    return;
                }

                // Chronicle completed successfully: mark this chat as chronicle-archived.
                WriteChronicleMarker(cid);
                try { SessionContext.MarkChronicleRebuilt(cid); } catch { }

                // This chat no longer needs Chronicle prompting/baseline tracking.
                lock (Sync)
                {
                    _chronicleBaselineTurnsByChat.Remove(cid);
                    _chroniclePromptLastShownUtcByChat.Remove(cid);
                }
                // Replace the sticky "do not send" toast with the completion toast.
                ToastHub.TryShow(ToastKey.ChronicleCompleted, chatId: cid, bypassLaunchQuiet: true);

                TryAppendChronicleAudit(cid, $"Chronicle COMPLETED | Utc={DateTime.UtcNow:o} | Captured={captured} | Ms={ms}");
                OperationCoordinator.End(GuardedOperationKind.Chronicle);
            }
            catch
            {
                lock (Sync) { _chronicleInFlight = false; _chronicleRunning = false; _chronicleRequestId = null; }

                // If the completion handler failed unexpectedly, do not leave the sticky "do not send" toast up.
                try
                {
                    ToastHub.TryShow(
                        ToastKey.ActionUnavailable,
                        bypassLaunchQuiet: true,
                        groupKeyOverride: ToastGroup_Chronicle,
                        replaceGroupOverride: true,
                        bypassBurstDedupeOverride: true);
                }
                catch { }
            }
        }


        private static void TryDeleteDerivedArtifacts(string chatId)
        {
            try
            {
                var dir = TruthStorage.EnsureChatDir(chatId);
                string[] files =
                {
                    "Truth.view",
                    "Seed.log",
                    "RestructuredSeed.log",
                    "Essence-M.Pulse.txt"
                };

                foreach (var f in files)
                {
                    try
                    {
                        var p = Path.Combine(dir, f);
                        if (File.Exists(p)) File.Delete(p);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void TryAppendChronicleAudit(string chatId, string line)
        {
            try
            {
                var dir = TruthStorage.EnsureChatDir(chatId);
                var path = Path.Combine(dir, "Chronicle.audit.txt");
                AtomicFile.TryAppendAllText(path, (line ?? string.Empty).Trim() + Environment.NewLine);
            }
            catch { }
        }

        private static void HandleOpenSessionFolder(string? chatId)
        {
            var cid = SessionContext.ResolveChatId(chatId);
            if (string.IsNullOrWhiteSpace(cid))
                return;

            try
            {
                var dir = TruthStorage.EnsureChatDir(cid);
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
            catch
            {
                ToastHub.TryShow(ToastKey.ActionUnavailable, bypassLaunchQuiet: true);
            }
        }

        private static int nthIndexOf(string s, char c, int n)
        {
            int count = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == c)
                {
                    count++;
                    if (count == n) return i;
                }
            }
            return -1;
        }
    }
}
