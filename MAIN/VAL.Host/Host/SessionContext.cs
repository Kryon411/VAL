using System;
using System.Collections.Generic;

namespace VAL.Host
{
    /// <summary>
    /// SessionContext is the single authoritative place for "what chat are we in".
    ///
    /// It is intentionally small and resilient:
    /// - Stores the last non-empty chatId observed from any WebView message.
    /// - Exposes a best-effort "attached" signal once we've seen any chatId.
    ///
    /// This removes duplicated "active chat" tracking across modules.
    /// </summary>
    public static class SessionContext
    {
        private static readonly object Sync = new object();

        private static string _activeChatId = string.Empty;
        private static bool _sessionAttached;
        private static DateTime _lastChatIdUtc = DateTime.MinValue;

        // ---------------------------------
        // Per-chat metadata (best-effort)
        // ---------------------------------
        private sealed class ChatMeta
        {
            public ChatOrigin Origin = ChatOrigin.Unknown;
            public bool WasMissingTruthLogAtAttach;
            public bool HasEssenceInjection;
            public DateTime LastTouchedUtc = DateTime.MinValue;
        }

        private static readonly Dictionary<string, ChatMeta> MetaByChatId =
            new Dictionary<string, ChatMeta>(StringComparer.OrdinalIgnoreCase);

        private static ChatMeta GetOrCreateMetaLocked(string chatId)
        {
            if (!MetaByChatId.TryGetValue(chatId, out var meta))
            {
                meta = new ChatMeta();
                MetaByChatId[chatId] = meta;
            }

            meta.LastTouchedUtc = DateTime.UtcNow;
            return meta;
        }

        /// <summary>
        /// The most recently observed chatId (best-effort). May be empty early in startup.
        /// </summary>
        public static string ActiveChatId
        {
            get
            {
                lock (Sync)
                {
                    return _activeChatId;
                }
            }
        }

        /// <summary>
        /// True once the host has observed any message containing a chatId.
        /// (Used as a coarse readiness gate for long-running operations.)
        /// </summary>
        public static bool IsSessionAttached
        {
            get
            {
                lock (Sync)
                {
                    return _sessionAttached;
                }
            }
        }

        public static DateTime LastChatIdUtc
        {
            get
            {
                lock (Sync)
                {
                    return _lastChatIdUtc;
                }
            }
        }

        /// <summary>
        /// Observe an inbound WebView message and capture chat context if present.
        /// Safe to call for every message.
        /// </summary>
        public static void Observe(string? type, string? chatId)
        {
            // For now, "attached" is intentionally loose: any chatId implies we have a usable session context.
            // (Continuum still applies its own deeper gating for Pulse/Chronicle availability.)
            if (!string.IsNullOrWhiteSpace(chatId))
            {
                SetActiveChatId(chatId);
                return;
            }

            // Reserved: in the future we may treat specific types as attach/detach signals.
            _ = type;
        }

        public static void SetActiveChatId(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                return;

            lock (Sync)
            {
                _activeChatId = chatId.Trim();
                _sessionAttached = true;
                _lastChatIdUtc = DateTime.UtcNow;
            }
        }

        // ---------------------------------
        // Chat origin / genesis mode
        // ---------------------------------

        /// <summary>
        /// Ensure a meta record exists for this chat and default Origin to Organic
        /// (unless it has already been classified).
        /// </summary>
        public static void EnsureInitialized(string? chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return;
            var cid = chatId.Trim();
            if (cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase)) return;

            lock (Sync)
            {
                var meta = GetOrCreateMetaLocked(cid);
                if (meta.Origin == ChatOrigin.Unknown)
                    meta.Origin = ChatOrigin.Organic;
            }
        }

        public static ChatOrigin GetOrigin(string? chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return ChatOrigin.Unknown;
            var cid = chatId.Trim();
            if (cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase)) return ChatOrigin.Unknown;

            lock (Sync)
            {
                return MetaByChatId.TryGetValue(cid, out var meta) ? meta.Origin : ChatOrigin.Unknown;
            }
        }

        public static void SetOrigin(string? chatId, ChatOrigin origin)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return;
            var cid = chatId.Trim();
            if (cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase)) return;

            lock (Sync)
            {
                var meta = GetOrCreateMetaLocked(cid);
                meta.Origin = origin;
                if (origin == ChatOrigin.ContinuumSeeded)
                    meta.HasEssenceInjection = true;
            }
        }

        public static bool HasEssenceInjection(string? chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return false;
            var cid = chatId.Trim();
            if (cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase)) return false;

            lock (Sync)
            {
                return MetaByChatId.TryGetValue(cid, out var meta) && meta.HasEssenceInjection;
            }
        }

        public static void MarkContinuumSeeded(string? chatId)
        {
            SetOrigin(chatId, ChatOrigin.ContinuumSeeded);
        }

        public static void MarkChronicleRebuilt(string? chatId)
        {
            SetOrigin(chatId, ChatOrigin.ChronicleRebuilt);
        }

        public static bool WasMissingTruthLogAtAttach(string? chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return false;
            var cid = chatId.Trim();
            if (cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase)) return false;

            lock (Sync)
            {
                return MetaByChatId.TryGetValue(cid, out var meta) && meta.WasMissingTruthLogAtAttach;
            }
        }

        public static void SetMissingTruthLogAtAttach(string? chatId, bool missing)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return;
            var cid = chatId.Trim();
            if (cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase)) return;

            lock (Sync)
            {
                var meta = GetOrCreateMetaLocked(cid);
                meta.WasMissingTruthLogAtAttach = missing;
            }
        }

        /// <summary>
        /// Returns chatId if provided; otherwise falls back to ActiveChatId.
        /// </summary>
        public static string ResolveChatId(string? chatId)
        {
            if (!string.IsNullOrWhiteSpace(chatId))
                return chatId!.Trim();

            return ActiveChatId;
        }

        /// <summary>
        /// Returns false for the synthetic "session-*" placeholder chat IDs.
        /// </summary>
        public static bool IsValidChatId(string? chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                return false;

            var cid = chatId.Trim();
            if (cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }
    }
}
