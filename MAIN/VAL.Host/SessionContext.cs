using System;
using System.Collections.Generic;
using VAL.Host.Services;

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
    public sealed class SessionContext : ISessionContext
    {
        private readonly object _sync = new object();

        private string _activeChatId = string.Empty;
        private bool _sessionAttached;
        private DateTime _lastChatIdUtc = DateTime.MinValue;

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

        private readonly Dictionary<string, ChatMeta> _metaByChatId =
            new Dictionary<string, ChatMeta>(StringComparer.OrdinalIgnoreCase);

        private ChatMeta GetOrCreateMetaLocked(string chatId)
        {
            if (!_metaByChatId.TryGetValue(chatId, out var meta))
            {
                meta = new ChatMeta();
                _metaByChatId[chatId] = meta;
            }

            meta.LastTouchedUtc = DateTime.UtcNow;
            return meta;
        }

        /// <summary>
        /// The most recently observed chatId (best-effort). May be empty early in startup.
        /// </summary>
        public string ActiveChatId
        {
            get
            {
                lock (_sync)
                {
                    return _activeChatId;
                }
            }
        }

        /// <summary>
        /// True once the host has observed any message containing a chatId.
        /// (Used as a coarse readiness gate for long-running operations.)
        /// </summary>
        public bool IsSessionAttached
        {
            get
            {
                lock (_sync)
                {
                    return _sessionAttached;
                }
            }
        }

        public DateTime LastChatIdUtc
        {
            get
            {
                lock (_sync)
                {
                    return _lastChatIdUtc;
                }
            }
        }

        /// <summary>
        /// Observe an inbound WebView message and capture chat context if present.
        /// Safe to call for every message.
        /// </summary>
        public void Observe(string? type, string? chatId)
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

        public void SetActiveChatId(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                return;

            lock (_sync)
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
        public void EnsureInitialized(string? chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return;
            var cid = chatId.Trim();
            if (cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase)) return;

            lock (_sync)
            {
                var meta = GetOrCreateMetaLocked(cid);
                if (meta.Origin == ChatOrigin.Unknown)
                    meta.Origin = ChatOrigin.Organic;
            }
        }

        public ChatOrigin GetOrigin(string? chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return ChatOrigin.Unknown;
            var cid = chatId.Trim();
            if (cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase)) return ChatOrigin.Unknown;

            lock (_sync)
            {
                return _metaByChatId.TryGetValue(cid, out var meta) ? meta.Origin : ChatOrigin.Unknown;
            }
        }

        private void SetOrigin(string? chatId, ChatOrigin origin)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return;
            var cid = chatId.Trim();
            if (cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase)) return;

            lock (_sync)
            {
                var meta = GetOrCreateMetaLocked(cid);
                meta.Origin = origin;
                if (origin == ChatOrigin.ContinuumSeeded)
                    meta.HasEssenceInjection = true;
            }
        }

        public bool HasEssenceInjection(string? chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return false;
            var cid = chatId.Trim();
            if (cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase)) return false;

            lock (_sync)
            {
                return _metaByChatId.TryGetValue(cid, out var meta) && meta.HasEssenceInjection;
            }
        }

        public void MarkContinuumSeeded(string? chatId)
        {
            SetOrigin(chatId, ChatOrigin.ContinuumSeeded);
        }

        public void MarkChronicleRebuilt(string? chatId)
        {
            SetOrigin(chatId, ChatOrigin.ChronicleRebuilt);
        }

        public bool WasMissingTruthLogAtAttach(string? chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return false;
            var cid = chatId.Trim();
            if (cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase)) return false;

            lock (_sync)
            {
                return _metaByChatId.TryGetValue(cid, out var meta) && meta.WasMissingTruthLogAtAttach;
            }
        }

        public void SetMissingTruthLogAtAttach(string? chatId, bool missing)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return;
            var cid = chatId.Trim();
            if (cid.StartsWith("session-", StringComparison.OrdinalIgnoreCase)) return;

            lock (_sync)
            {
                var meta = GetOrCreateMetaLocked(cid);
                meta.WasMissingTruthLogAtAttach = missing;
            }
        }

        /// <summary>
        /// Returns chatId if provided; otherwise falls back to ActiveChatId.
        /// </summary>
        public string ResolveChatId(string? chatId)
        {
            if (!string.IsNullOrWhiteSpace(chatId))
                return chatId!.Trim();

            return ActiveChatId;
        }

        /// <summary>
        /// Returns false for the synthetic "session-*" placeholder chat IDs.
        /// </summary>
        public bool IsValidChatId(string? chatId)
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
