using System;
using System.Collections.Generic;
using System.Windows;
using VAL.Continuum.Pipeline;

namespace VAL.Host
{
    /// <summary>
    /// ToastHub: central policy + routing layer.
    /// This file is intentionally self-contained (no external ToastDefinition dependency),
    /// so changes here cannot break upstream Continuum logic.
    /// </summary>
    public static class ToastHub
    {
        // -----------------------------
        // Internal definition container
        // -----------------------------
        private sealed record ToastDef(
            ToastKey Key,
            string Title,
            string? Subtitle,
            ToastManager.ToastDurationBucket Duration,
            string? GroupKey,
            bool ReplaceGroup,
            bool BypassBurstDedupe,
            bool IsPassive,
            bool OncePerChat,
            string? LedgerId,
            TimeSpan Cooldown
        );

        private static readonly object Gate = new object();
        // Cooldown de-dupe is per (key + context) so chat-specific nudges don't suppress each other.
        private static readonly Dictionary<string, DateTime> _lastShownUtc = new();

        // Central catalog (policy table).
        private static readonly Dictionary<ToastKey, ToastDef> Defs =
            new()
            {
                // -----------------
                // Host / global
                // -----------------
                {
                    ToastKey.VoidEnabled,
                    new ToastDef(
                        ToastKey.VoidEnabled,
                        "Void is now on. Screenshots and code blocks will be hidden to keep the conversation easy to read.",
                        null,
                        ToastManager.ToastDurationBucket.L,
                        "void",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(1))
                },
                {
                    ToastKey.VoidDisabled,
                    new ToastDef(
                        ToastKey.VoidDisabled,
                        "Void is now off. All screenshots and code blocks are visible again.",
                        null,
                        ToastManager.ToastDurationBucket.M,
                        "void",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(1))
                },

                // -----------------
                // Continuum lifecycle
                // -----------------
                {
                    ToastKey.ContinuumArchivingPaused,
                    new ToastDef(
                        ToastKey.ContinuumArchivingPaused,
                        "Continuum has been paused and archiving has stopped.",
                        null,
                        ToastManager.ToastDurationBucket.M,
                        "continuum.lifecycle",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(2))
                },

                // -----------------
                // Continuum guidance
                // -----------------
                {
                    ToastKey.PreludeAvailable,
                    new ToastDef(
                        ToastKey.PreludeAvailable,
                        "This chat can be prepared for continuation. If you’d like, Prelude can set things up so a future Pulse jump has the right context.",
                        null,
                        ToastManager.ToastDurationBucket.L,
                        "continuum_guidance",
                        ReplaceGroup: false,
                        BypassBurstDedupe: false,
                        IsPassive: true,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.Zero)
                },
                {
                    ToastKey.PreludePrompt,
                    new ToastDef(
                        ToastKey.PreludePrompt,
                        "Starting a new chat?",
                        "Prelude can set up this chat for continuation. If you’d like, it will insert setup and instructions so a future Pulse jump has the right context.",
                        ToastManager.ToastDurationBucket.Sticky, // ShowActions controls duration; keep a sane default.
                        "continuum_guidance",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(3))
                },
                {
                    ToastKey.ChroniclePrompt,
                    new ToastDef(
                        ToastKey.ChroniclePrompt,
                        "Want to save this chat?",
                        "Chronicle can rebuild a local Truth log so future Pulse jumps have the right context.",
                        ToastManager.ToastDurationBucket.Sticky,
                        "continuum_guidance",
                        ReplaceGroup: false,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: "guidance.chronicle_action_prompt",
                        Cooldown: TimeSpan.Zero)
                },
                {
                    ToastKey.ChronicleSuggested,
                    new ToastDef(
                        ToastKey.ChronicleSuggested,
                        "VAL has detected you’re continuing in a chat without an archive. Chronicle can rebuild one to help maintain context for Pulse jumps. Please select Chronicle in the Control Centre.",
                        null,
                        ToastManager.ToastDurationBucket.XL,
                        "continuum_guidance",
                        ReplaceGroup: false,
                        BypassBurstDedupe: false,
                        IsPassive: true,
                        OncePerChat: true,
                        LedgerId: "guidance.chronicle_suggested",
                        Cooldown: TimeSpan.Zero)
                },

                // -----------------
                // Pulse
                // -----------------
                {
                    ToastKey.PulseInitiated,
                    new ToastDef(
                        ToastKey.PulseInitiated,
                        "A Pulse jump has been initiated. Please stand by.",
                        null,
                        ToastManager.ToastDurationBucket.XS,
                        "pulse",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.Zero)
                },
                {
                    ToastKey.PulseReady,
                    new ToastDef(
                        ToastKey.PulseReady,
                        "Your Pulse jump is ready. Please hit Send to finalize and continue.",
                        null,
                        ToastManager.ToastDurationBucket.M,
                        "pulse",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.Zero)
                },
                {
                    ToastKey.PulseAlreadyRunning,
                    new ToastDef(
                        ToastKey.PulseAlreadyRunning,
                        "A Pulse jump is already in progress. Please wait a moment for it to finish.",
                        null,
                        ToastManager.ToastDurationBucket.M,
                        "pulse",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(2))
                },
                {
                    ToastKey.PulseUnavailable,
                    new ToastDef(
                        ToastKey.PulseUnavailable,
                        "Pulse can’t be used in this chat yet. Preparing the chat with Chronicle will make Pulse jumps available.",
                        null,
                        ToastManager.ToastDurationBucket.L,
                        "pulse",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(2))
                },
                {
                    ToastKey.PulseNoTruthLogFound,
                    new ToastDef(
                        ToastKey.PulseNoTruthLogFound,
                        "There’s no archive for this chat yet. Running Chronicle will create one so Pulse jumps can work properly.",
                        null,
                        ToastManager.ToastDurationBucket.L,
                        "pulse",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: true,
                        LedgerId: "guidance.no_truth_log_found",
                        Cooldown: TimeSpan.Zero)
                },

                // -----------------
                // Chronicle
                // -----------------
                {
                    ToastKey.ChronicleUnavailable,
                    new ToastDef(
                        ToastKey.ChronicleUnavailable,
                        "Chronicle can only be used in an existing chat. Please open the conversation you want to archive and try again.",
                        null,
                        ToastManager.ToastDurationBucket.M,
                        "chronicle",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(2))
                },
                {
                    ToastKey.ChronicleStarted,
                    new ToastDef(
                        ToastKey.ChronicleStarted,
                        "Chronicle has started. VAL is rebuilding an archive for this chat — please do not send messages until Chronicle is complete.",
                        null,
                        ToastManager.ToastDurationBucket.Sticky,
                        "chronicle",
                        ReplaceGroup: true,
                        BypassBurstDedupe: true,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.Zero)
                },
                {
                    ToastKey.ChronicleCompleted,
                    new ToastDef(
                        ToastKey.ChronicleCompleted,
                        "Chronicle is complete. This chat is now archived and ready for Pulse jumps.",
                        null,
                        ToastManager.ToastDurationBucket.M,
                        "chronicle",
                        ReplaceGroup: true,
                        BypassBurstDedupe: true,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.Zero)
                },

                // -----------------
                // Abyss recall/search
                // -----------------
                {
                    ToastKey.AbyssSearching,
                    new ToastDef(
                        ToastKey.AbyssSearching,
                        "Abyss: searching…",
                        null,
                        ToastManager.ToastDurationBucket.XS,
                        "abyss",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(1))
                },
                {
                    ToastKey.AbyssMatches,
                    new ToastDef(
                        ToastKey.AbyssMatches,
                        "Abyss: matches ready.",
                        null,
                        ToastManager.ToastDurationBucket.M,
                        "abyss",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(1))
                },
                {
                    ToastKey.AbyssNoMatches,
                    new ToastDef(
                        ToastKey.AbyssNoMatches,
                        "Abyss: no matches found.",
                        null,
                        ToastManager.ToastDurationBucket.M,
                        "abyss",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(1))
                },
                {
                    ToastKey.AbyssNoTruthLogs,
                    new ToastDef(
                        ToastKey.AbyssNoTruthLogs,
                        "Abyss: no Truth.log files found.",
                        null,
                        ToastManager.ToastDurationBucket.L,
                        "abyss",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(2))
                },
                {
                    ToastKey.AbyssNoQuery,
                    new ToastDef(
                        ToastKey.AbyssNoQuery,
                        "Abyss: enter a search query first.",
                        null,
                        ToastManager.ToastDurationBucket.S,
                        "abyss",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(2))
                },
                {
                    ToastKey.AbyssResultsWritten,
                    new ToastDef(
                        ToastKey.AbyssResultsWritten,
                        "Abyss: wrote Abyss.Results.txt",
                        null,
                        ToastManager.ToastDurationBucket.S,
                        "abyss",
                        ReplaceGroup: false,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(1))
                },
                {
                    ToastKey.AbyssInjected,
                    new ToastDef(
                        ToastKey.AbyssInjected,
                        "Abyss: injected result.",
                        null,
                        ToastManager.ToastDurationBucket.S,
                        "abyss",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(1))
                },
                {
                    ToastKey.AbyssNoSelection,
                    new ToastDef(
                        ToastKey.AbyssNoSelection,
                        "Abyss: choose a result number to inject.",
                        null,
                        ToastManager.ToastDurationBucket.S,
                        "abyss",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(1))
                },

                // -----------------
                // Generic / guard rails
                // -----------------
                {
                    ToastKey.ActionUnavailable,
                    new ToastDef(
                        ToastKey.ActionUnavailable,
                        "That action isn’t available right now. Please try again in a moment.",
                        null,
                        ToastManager.ToastDurationBucket.S,
                        null,
                        ReplaceGroup: false,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(1))
                },
                {
                    ToastKey.OperationInProgress,
                    new ToastDef(
                        ToastKey.OperationInProgress,
                        "An operation is already in progress.",
                        null,
                        ToastManager.ToastDurationBucket.S,
                        "op.guard",
                        ReplaceGroup: true,
                        BypassBurstDedupe: true,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(2))
                },
                {
                    ToastKey.OperationCancelled,
                    new ToastDef(
                        ToastKey.OperationCancelled,
                        "Operation cancelled.",
                        null,
                        ToastManager.ToastDurationBucket.S,
                        "op.guard",
                        ReplaceGroup: true,
                        BypassBurstDedupe: true,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(2))
                },

                // -----------------
                // Data wipe
                // -----------------
                {
                    ToastKey.DataWipeCompleted,
                    new ToastDef(
                        ToastKey.DataWipeCompleted,
                        "Data wipe complete. Logs, profiles, and local memory have been cleared.",
                        "Privacy settings were preserved.",
                        ToastManager.ToastDurationBucket.M,
                        "data_wipe",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(1))
                },
                {
                    ToastKey.DataWipePartial,
                    new ToastDef(
                        ToastKey.DataWipePartial,
                        "Data wipe completed with some locked files.",
                        "Close any open logs or folders and try again.",
                        ToastManager.ToastDurationBucket.M,
                        "data_wipe",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: false,
                        OncePerChat: false,
                        LedgerId: null,
                        Cooldown: TimeSpan.FromSeconds(2))
                },

                // -----------------
                // Telemetry
                // -----------------
                {
                    ToastKey.TelemetrySessionSizeEarly,
                    new ToastDef(
                        ToastKey.TelemetrySessionSizeEarly,
                        "VAL has detected this conversation may soon affect performance. A Pulse jump is recommended. Clicking the Pulse button in the Control Centre will move you into a new chat with the context intact.",
                        null,
                        ToastManager.ToastDurationBucket.L,
                        "telemetry",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: true,
                        OncePerChat: true,
                        LedgerId: "telemetry.session_size.early",
                        Cooldown: TimeSpan.Zero)
                },
                {
                    ToastKey.TelemetrySessionSizeLarge,
                    new ToastDef(
                        ToastKey.TelemetrySessionSizeLarge,
                        "This conversation has grown large and may begin to feel slower. A Pulse jump is strongly recommended to carry the important context forward while keeping things responsive.",
                        null,
                        ToastManager.ToastDurationBucket.XL,
                        "telemetry",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: true,
                        OncePerChat: true,
                        LedgerId: "telemetry.session_size.large",
                        Cooldown: TimeSpan.Zero)
                },
                {
                    ToastKey.TelemetrySessionSizeVeryLarge,
                    new ToastDef(
                        ToastKey.TelemetrySessionSizeVeryLarge,
                        "This conversation is now very large and performance may be impacted. Using a Pulse jump will help maintain context and restore responsiveness.",
                        null,
                        ToastManager.ToastDurationBucket.XL,
                        "telemetry",
                        ReplaceGroup: true,
                        BypassBurstDedupe: false,
                        IsPassive: true,
                        OncePerChat: true,
                        LedgerId: "telemetry.session_size.very_large",
                        Cooldown: TimeSpan.Zero)
                },
            };

        public static void Initialize(Window w) => ToastManager.Initialize(w);

        /// <summary>
        /// Standard (catalog) toasts. Returns true if a toast was dispatched.
        /// </summary>
        public static bool TryShow(
            ToastKey key,
            string? chatId = null,
            bool bypassLaunchQuiet = false,
            string? titleOverride = null,
            string? subtitleOverride = null,
            ToastManager.ToastDurationBucket? durationOverride = null,
            string? groupKeyOverride = null,
            bool? replaceGroupOverride = null,
            bool? bypassBurstDedupeOverride = null,
            bool? oncePerChatOverride = null,
            string? ledgerIdOverride = null)
        {
            if (!Defs.TryGetValue(key, out var def))
                return false;

            // Launch quiet period applies only to passive/system nudges.
            if (def.IsPassive && !bypassLaunchQuiet && ToastManager.IsLaunchQuietPeriodActive)
                return false;

            // Once-per-chat gating (persisted via ToastLedger).
            var oncePerChat = oncePerChatOverride ?? def.OncePerChat;
            if (oncePerChat)
            {
                if (string.IsNullOrWhiteSpace(chatId))
                    return false;

                var lid = ledgerIdOverride ?? def.LedgerId ?? ("toast." + key);
                if (!ToastLedger.TryMarkShown(chatId, lid))
                    return false;
            }

            var title = titleOverride ?? def.Title;
            var subtitle = subtitleOverride ?? def.Subtitle;
            var duration = durationOverride ?? def.Duration;
            var groupKey = groupKeyOverride ?? def.GroupKey;
            var replaceGroup = replaceGroupOverride ?? def.ReplaceGroup;
            var bypassBurstDedupe = bypassBurstDedupeOverride ?? def.BypassBurstDedupe;

            // Cooldown (best-effort per key + context).
            if (def.Cooldown > TimeSpan.Zero)
            {
                var now = DateTime.UtcNow;
                var cooldownKey = key.ToString();
                if (!string.IsNullOrWhiteSpace(chatId)) cooldownKey += "|" + chatId.Trim();
                else if (!string.IsNullOrWhiteSpace(groupKey)) cooldownKey += "|" + groupKey;

                lock (Gate)
                {
                    if (_lastShownUtc.TryGetValue(cooldownKey, out var last))
                    {
                        if ((now - last) < def.Cooldown)
                            return false;
                    }

                    _lastShownUtc[cooldownKey] = now;
                }
            }

            ToastManager.ShowCatalog(
                title,
                subtitle,
                duration,
                groupKey,
                replaceGroup,
                bypassBurstDedupe);

            return true;
        }

        /// <summary>
        /// Action toasts (buttons). Gating mirrors TryShow.
        /// </summary>
        public static bool TryShowActions(
            ToastKey key,
            (string Label, Action OnClick)[] actions,
            string? chatId = null,
            bool bypassLaunchQuiet = false,
            string? titleOverride = null,
            string? subtitleOverride = null,
            string? groupKeyOverride = null,
            bool? replaceGroupOverride = null,
            bool? oncePerChatOverride = null,
            string? ledgerIdOverride = null)
        {
            if (!Defs.TryGetValue(key, out var def))
                return false;

            if (def.IsPassive && !bypassLaunchQuiet && ToastManager.IsLaunchQuietPeriodActive)
                return false;

            var oncePerChat = oncePerChatOverride ?? def.OncePerChat;
            if (oncePerChat)
            {
                if (string.IsNullOrWhiteSpace(chatId))
                    return false;

                var lid = ledgerIdOverride ?? def.LedgerId ?? ("toast." + key);
                if (!ToastLedger.TryMarkShown(chatId, lid))
                    return false;
            }

            var title = titleOverride ?? def.Title;
            var subtitle = subtitleOverride ?? def.Subtitle ?? string.Empty;
            var groupKey = groupKeyOverride ?? def.GroupKey;
            var replaceGroup = replaceGroupOverride ?? def.ReplaceGroup;

            if (def.Cooldown > TimeSpan.Zero)
            {
                var now = DateTime.UtcNow;
                var cooldownKey = key.ToString();
                if (!string.IsNullOrWhiteSpace(chatId)) cooldownKey += "|" + chatId.Trim();
                else if (!string.IsNullOrWhiteSpace(groupKey)) cooldownKey += "|" + groupKey;

                lock (Gate)
                {
                    if (_lastShownUtc.TryGetValue(cooldownKey, out var last))
                    {
                        if ((now - last) < def.Cooldown)
                            return false;
                    }

                    _lastShownUtc[cooldownKey] = now;
                }
            }

            var sticky = def.Duration == ToastManager.ToastDurationBucket.Sticky;
            ToastManager.ShowActions(title, subtitle, groupKey, replaceGroup, sticky, actions);
            return true;
        }

        public static void TryShowOperationCancelled(string groupKey)
        {
            // Use the OperationCancelled definition, but let callers choose the group bucket.
            TryShow(
                ToastKey.OperationCancelled,
                chatId: null,
                bypassLaunchQuiet: true,
                groupKeyOverride: groupKey,
                replaceGroupOverride: true,
                bypassBurstDedupeOverride: true);
        }
    }
}
