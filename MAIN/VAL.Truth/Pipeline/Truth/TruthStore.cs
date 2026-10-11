using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

using VAL.Continuum.Pipeline;

namespace VAL.Continuum.Pipeline.Truth
{
    public sealed class TruthStore : ITruthStore, IDisposable
    {
        public const string DefaultTruthFileName = "Truth.log";

        private sealed class RebuildSession
        {
            public readonly System.Collections.Generic.HashSet<string> Seen =
                new(StringComparer.Ordinal);
            public string TempPath = string.Empty;
            public CancellationToken Token;
            public string BackupPath = string.Empty;
        }

        private sealed class ChatIndex
        {
            public readonly object Gate = new();
            public readonly System.Collections.Generic.HashSet<string> Seen =
                new(StringComparer.Ordinal);
            public bool Loaded;
        }

        private readonly ITruthTelemetryPublisher _telemetryPublisher;
        private readonly string _memoryChatsRoot;
        private readonly ConcurrentDictionary<string, ChatIndex> _indexByChat =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, RebuildSession> _rebuildByChat =
            new(StringComparer.OrdinalIgnoreCase);

        public TruthStore(ITruthTelemetryPublisher telemetryPublisher)
            : this(telemetryPublisher, memoryChatsRootOverride: null)
        {
        }

        public TruthStore(ITruthTelemetryPublisher telemetryPublisher, string? memoryChatsRootOverride)
        {
            _telemetryPublisher = telemetryPublisher ?? throw new ArgumentNullException(nameof(telemetryPublisher));
            _memoryChatsRoot = ResolveMemoryChatsRoot(memoryChatsRootOverride);
        }

        public string TruthFileName => DefaultTruthFileName;

        public bool AppendTruthLine(string chatId, char role, string text)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return false;
            if (string.IsNullOrWhiteSpace(text)) return false;

            try
            {
                EnsureChatDir(chatId);

                var roleChar = (role == 'A' || role == 'a') ? 'A' : 'U';
                var normalized = NormalizeForStorage(text);
                if (string.IsNullOrWhiteSpace(normalized)) return false;
                var fp = Fingerprint(roleChar, NormalizeForFingerprint(normalized));
                var line = (roleChar == 'A' ? "A|" : "U|") + normalized + Environment.NewLine;

                var idx = _indexByChat.GetOrAdd(chatId, _ => new ChatIndex());
                // One gate owns appends, rebuild transitions, resets, and archive reads.
                lock (idx.Gate)
                {
                    if (_rebuildByChat.TryGetValue(chatId, out var rebuild))
                    {
                        if (rebuild.Token.IsCancellationRequested)
                            return false;

                        if (rebuild.Seen.Contains(fp))
                            return false;

                        if (!AtomicFile.TryAppendAllText(rebuild.TempPath, line))
                            return false;
                        rebuild.Seen.Add(fp);
                        PublishTelemetry(chatId);
                        return true;
                    }
                    try
                    {
                        var path = GetTruthPath(chatId);
                        if (idx.Loaded && !File.Exists(path) && idx.Seen.Count > 0)
                            idx.Seen.Clear();
                    }
                    catch
                    {
                    }

                    EnsureIndexLoaded(chatId, idx);

                    if (idx.Seen.Contains(fp))
                        return false;

                    if (!AtomicFile.TryAppendAllText(GetTruthPath(chatId), line))
                        return false;

                    idx.Seen.Add(fp);
                    PublishTelemetry(chatId);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public string GetChatDir(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                throw new ArgumentNullException(nameof(chatId));

            if (chatId is "." or ".." || Path.IsPathRooted(chatId) ||
                chatId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                chatId.EndsWith('.') || chatId.EndsWith(' '))
                throw new ArgumentException("Chat IDs must be safe single directory names.", nameof(chatId));

            var stem = chatId.Split('.')[0];
            if (Regex.IsMatch(stem, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new ArgumentException("Chat IDs cannot use reserved device names.", nameof(chatId));

            return Path.Combine(_memoryChatsRoot, chatId);
        }

        public string GetTruthPath(string chatId)
        {
            return Path.Combine(GetChatDir(chatId), TruthFileName);
        }

        public string EnsureChatDir(string chatId)
        {
            var dir = GetChatDir(chatId);
            Directory.CreateDirectory(dir);
            return dir;
        }

        public bool TryBeginTruthRebuild(string chatId, bool backupExisting, out string backupPath, out string tempTruthPath, CancellationToken token)
        {
            backupPath = string.Empty;
            tempTruthPath = string.Empty;

            if (string.IsNullOrWhiteSpace(chatId)) return false;

            try
            {
                EnsureChatDir(chatId);

                var idx = _indexByChat.GetOrAdd(chatId, _ => new ChatIndex());
                lock (idx.Gate)
                {
                    if (token.IsCancellationRequested || _rebuildByChat.ContainsKey(chatId))
                        return false;

                    var finalPath = GetTruthPath(chatId);
                    var dir = Path.GetDirectoryName(finalPath) ?? EnsureChatDir(chatId);

                    if (backupExisting && File.Exists(finalPath))
                    {
                        var dest = GetBackupPath(dir);
                        File.Copy(finalPath, dest, overwrite: false);
                        backupPath = dest;
                    }

                    tempTruthPath = Path.Combine(dir, TruthFileName + ".tmp." + Guid.NewGuid().ToString("N"));
                    AtomicFile.WriteAllTextAtomic(tempTruthPath, string.Empty);

                    var session = new RebuildSession
                    {
                        TempPath = tempTruthPath,
                        Token = token,
                        BackupPath = backupPath
                    };

                    _rebuildByChat[chatId] = session;

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public void AbortTruthRebuild(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return;

            try
            {
                GetChatDir(chatId);
                var idx = _indexByChat.GetOrAdd(chatId, _ => new ChatIndex());
                lock (idx.Gate)
                {
                    if (_rebuildByChat.TryRemove(chatId, out var session))
                    {
                        try { File.Delete(session.TempPath); } catch { }
                    }
                }
            }
            catch
            {
            }
        }

        public void AbortAllTruthRebuilds()
        {
            try
            {
                foreach (var kv in _rebuildByChat)
                {
                    try { AbortTruthRebuild(kv.Key); } catch { }
                }
            }
            catch
            {
            }
        }

        public bool TryCommitTruthRebuild(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return false;

            try
            {
                GetChatDir(chatId);
                var idx = _indexByChat.GetOrAdd(chatId, _ => new ChatIndex());
                lock (idx.Gate)
                {
                    if (!_rebuildByChat.TryGetValue(chatId, out var session))
                        return false;

                    if (session.Token.IsCancellationRequested)
                    {
                        AbortTruthRebuild(chatId);
                        return false;
                    }

                    var finalPath = GetTruthPath(chatId);
                    if (string.IsNullOrWhiteSpace(session.TempPath) || !File.Exists(session.TempPath))
                        return false;
                    if (new FileInfo(session.TempPath).Length == 0)
                        return false;

                    AtomicFile.ReplaceAtomic(session.TempPath, finalPath);
                    _rebuildByChat.TryRemove(chatId, out _);
                    idx.Seen.Clear();
                    idx.Loaded = false;
                    PublishTelemetry(chatId);

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public bool TryResetTruthLog(string chatId, bool backupExisting, out string backupPath)
        {
            backupPath = string.Empty;
            if (string.IsNullOrWhiteSpace(chatId)) return false;

            try
            {
                EnsureChatDir(chatId);
                var idx = _indexByChat.GetOrAdd(chatId, _ => new ChatIndex());
                lock (idx.Gate)
                {
                    if (_rebuildByChat.ContainsKey(chatId))
                        return false;

                    var path = GetTruthPath(chatId);
                    if (File.Exists(path) && backupExisting)
                    {
                        var dir = Path.GetDirectoryName(path) ?? EnsureChatDir(chatId);
                        var dest = GetBackupPath(dir);
                        File.Move(path, dest);
                        backupPath = dest;
                    }
                    else
                    {
                        File.Delete(path);
                    }

                    idx.Seen.Clear();
                    idx.Loaded = true;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public string ReadTruthText(string chatId)
        {
            var path = GetTruthPath(chatId);
            var idx = _indexByChat.GetOrAdd(chatId, _ => new ChatIndex());
            lock (idx.Gate)
            {
                if (!File.Exists(path))
                    return string.Empty;

                var sb = new StringBuilder();
                foreach (var entry in TruthReader.Read(path, repairTailFirst: true))
                {
                    sb.Append(entry.Role);
                    sb.Append('|');
                    sb.Append(entry.Payload);
                    sb.Append(Environment.NewLine);
                }

                return sb.ToString();
            }
        }

        public string[] ReadTruthLines(string chatId)
        {
            var path = GetTruthPath(chatId);
            var idx = _indexByChat.GetOrAdd(chatId, _ => new ChatIndex());
            lock (idx.Gate)
            {
                if (!File.Exists(path))
                    return Array.Empty<string>();

                var lines = new System.Collections.Generic.List<string>();
                foreach (var entry in TruthReader.Read(path, repairTailFirst: true))
                    lines.Add($"{entry.Role}|{entry.Payload}");

                return lines.ToArray();
            }
        }

        private static string GetBackupPath(string directory)
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff", CultureInfo.InvariantCulture);
            return Path.Combine(directory, $"Truth.pre_chronicle.{stamp}.{Guid.NewGuid():N}.log");
        }

        private static string ResolveMemoryChatsRoot(string? memoryChatsRootOverride)
        {
            if (!string.IsNullOrWhiteSpace(memoryChatsRootOverride))
            {
                return Path.GetFullPath(memoryChatsRootOverride.Trim());
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VAL",
                "Memory",
                "Chats");
        }

        private static string NormalizeForStorage(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            s = s.Replace("\r\n", "\n").Replace("\r", "\n");
            s = StripUiChromeForStorage(s);
            s = s.Replace("\n", "\\n");
            return s.Trim();
        }

        private static string NormalizeForFingerprint(string storageNormalized)
        {
            if (string.IsNullOrEmpty(storageNormalized)) return string.Empty;

            try
            {
                var s = storageNormalized;
                s = s.Replace("\\r\\n", "\\n").Replace("\\r", "\\n");
                s = Regex.Replace(s, @"(\\n){2,}", "\\n");
                s = Regex.Replace(s, @"[ \t]{2,}", " ");
                return s.Trim();
            }
            catch
            {
                return (storageNormalized ?? string.Empty).Trim();
            }
        }

        private static string StripUiChromeForStorage(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            s = s.Replace("text\nCopy code\n", string.Empty);
            s = s.Replace("text\\nCopy code\\n", string.Empty);
            s = s.Replace("Copy code\n", string.Empty);
            s = s.Replace("Copy code\\n", string.Empty);
            s = s.Replace("\nCopy code", string.Empty);
            s = s.Replace("\\nCopy code", string.Empty);
            s = s.Replace("\nDocument\n", "\n");
            s = s.Replace("\\nDocument\\n", "\\n");

            var trimmed = s.TrimStart();
            if (trimmed.StartsWith("ChatGPT said:", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring("ChatGPT said:".Length);
            }
            else if (trimmed.StartsWith("ChatGPT said", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring("ChatGPT said".Length);
                trimmed = trimmed.TrimStart();
                if (trimmed.StartsWith(':'))
                    trimmed = trimmed.Substring(1);
            }

            trimmed = trimmed.TrimStart();
            if (trimmed.StartsWith("textVAL ", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(4);

            return trimmed;
        }

        private static string Fingerprint(char roleChar, string normalizedText)
        {
            var bytes = Encoding.UTF8.GetBytes(roleChar + "|" + normalizedText);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }

        private void EnsureIndexLoaded(string chatId, ChatIndex idx)
        {
            if (idx.Loaded) return;

            var path = GetTruthPath(chatId);
            if (!File.Exists(path))
            {
                idx.Loaded = true;
                return;
            }

            try
            {
                if (TruthFile.TryRepairTruncatedTail(path, out var removed) && removed > 0)
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrWhiteSpace(dir))
                        {
                            var logPath = Path.Combine(dir, "Truth.repair.log");
                            var line = FormattableString.Invariant($"{DateTime.UtcNow:O} truncated tail repair removed {removed} bytes{Environment.NewLine}");
                            AtomicFile.TryAppendAllText(logPath, line, durable: false);
                        }
                    }
                    catch
                    {
                    }
                }

                foreach (var entry in TruthReader.Read(path, repairTailFirst: false, throwOnOpenFailure: true))
                {
                    var txt = NormalizeForStorage(entry.Payload);
                    if (string.IsNullOrWhiteSpace(txt)) continue;

                    idx.Seen.Add(Fingerprint(entry.Role, NormalizeForFingerprint(txt)));
                }
            }
            catch
            {
                idx.Seen.Clear();
                throw;
            }
            idx.Loaded = true;
        }

        private void PublishTelemetry(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                return;

            long bytes = 0;
            try
            {
                var truthPath = GetTruthPath(chatId);
                if (File.Exists(truthPath))
                    bytes = new FileInfo(truthPath).Length;
            }
            catch
            {
                return;
            }

            try
            {
                _telemetryPublisher.PublishTruthBytes(chatId, bytes);
            }
            catch
            {
            }
        }

        public void Dispose() => AbortAllTruthRebuilds();
    }
}
