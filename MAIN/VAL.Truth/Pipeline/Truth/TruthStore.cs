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
    public sealed class TruthStore : ITruthStore
    {
        public const string DefaultTruthFileName = "Truth.log";

        private sealed class RebuildSession
        {
            public readonly object Gate = new();
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
            new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, RebuildSession> _rebuildByChat =
            new(StringComparer.Ordinal);

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

                if (_rebuildByChat.TryGetValue(chatId, out var rebuild))
                {
                    if (rebuild.Token.IsCancellationRequested)
                        return false;

                    lock (rebuild.Gate)
                    {
                        if (rebuild.Token.IsCancellationRequested)
                            return false;

                        var fp = Fingerprint(roleChar, NormalizeForFingerprint(normalized));
                        if (rebuild.Seen.Contains(fp))
                            return false;

                        var line = (roleChar == 'A' ? "A|" : "U|") + normalized + Environment.NewLine;
                        File.AppendAllText(rebuild.TempPath, line);
                        rebuild.Seen.Add(fp);
                        PublishTelemetry(chatId);
                        return true;
                    }
                }

                var idx = _indexByChat.GetOrAdd(chatId, _ => new ChatIndex());
                lock (idx.Gate)
                {
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

                    var fp = Fingerprint(roleChar, NormalizeForFingerprint(normalized));
                    if (idx.Seen.Contains(fp))
                        return false;

                    var line = (roleChar == 'A' ? "A|" : "U|") + normalized + Environment.NewLine;
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

            return Path.Combine(_memoryChatsRoot, chatId);
        }

        public string GetTruthPath(string chatId)
        {
            return Path.Combine(GetChatDir(chatId), TruthFileName);
        }

        public string EnsureChatDir(string chatId)
        {
            var dir = GetChatDir(chatId);
            try { Directory.CreateDirectory(dir); } catch { }
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

                if (_rebuildByChat.ContainsKey(chatId))
                    return false;

                var finalPath = GetTruthPath(chatId);
                var dir = Path.GetDirectoryName(finalPath) ?? EnsureChatDir(chatId);

                if (backupExisting && File.Exists(finalPath))
                {
                    var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                    var dest = Path.Combine(dir, $"Truth.pre_chronicle.{stamp}.log");
                    try
                    {
                        if (File.Exists(dest)) File.Delete(dest);
                        File.Copy(finalPath, dest, overwrite: true);
                        backupPath = dest;
                    }
                    catch
                    {
                    }
                }

                tempTruthPath = Path.Combine(dir, TruthFileName + ".tmp");
                if (File.Exists(tempTruthPath))
                {
                    try { File.Delete(tempTruthPath); } catch { }
                }

                var session = new RebuildSession
                {
                    TempPath = tempTruthPath,
                    Token = token,
                    BackupPath = backupPath
                };

                if (!_rebuildByChat.TryAdd(chatId, session))
                    return false;

                try
                {
                    AtomicFile.WriteAllTextAtomic(tempTruthPath, string.Empty);
                }
                catch
                {
                    AbortTruthRebuild(chatId);
                    return false;
                }

                return true;
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
                if (_rebuildByChat.TryRemove(chatId, out var session))
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(session.TempPath) && File.Exists(session.TempPath))
                            File.Delete(session.TempPath);
                    }
                    catch
                    {
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
                if (!_rebuildByChat.TryRemove(chatId, out var session))
                    return false;

                if (session.Token.IsCancellationRequested)
                {
                    try { if (File.Exists(session.TempPath)) File.Delete(session.TempPath); } catch { }
                    return false;
                }

                var finalPath = GetTruthPath(chatId);
                if (string.IsNullOrWhiteSpace(session.TempPath) || !File.Exists(session.TempPath))
                    return false;

                AtomicFile.ReplaceAtomic(session.TempPath, finalPath);

                try
                {
                    if (_indexByChat.TryGetValue(chatId, out var idx))
                    {
                        lock (idx.Gate)
                        {
                            idx.Seen.Clear();
                            idx.Loaded = false;
                        }
                    }
                }
                catch
                {
                }

                return true;
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

            try { EnsureChatDir(chatId); } catch { }

            try
            {
                var idx = _indexByChat.GetOrAdd(chatId, _ => new ChatIndex());
                lock (idx.Gate)
                {
                    idx.Seen.Clear();
                    idx.Loaded = true;

                    var path = GetTruthPath(chatId);
                    if (!File.Exists(path))
                        return true;

                    if (backupExisting)
                    {
                        var dir = Path.GetDirectoryName(path) ?? EnsureChatDir(chatId);
                        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                        var dest = Path.Combine(dir, $"Truth.pre_chronicle.{stamp}.log");

                        try { if (File.Exists(dest)) File.Delete(dest); } catch { }
                        try { File.Move(path, dest); backupPath = dest; } catch { }
                    }
                    else
                    {
                        try { File.Delete(path); } catch { }
                    }

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

        public string[] ReadTruthLines(string chatId)
        {
            var path = GetTruthPath(chatId);
            if (!File.Exists(path))
                return Array.Empty<string>();

            var lines = new System.Collections.Generic.List<string>();
            foreach (var entry in TruthReader.Read(path, repairTailFirst: true))
                lines.Add($"{entry.Role}|{entry.Payload}");

            return lines.ToArray();
        }

        private static string ResolveMemoryChatsRoot(string? memoryChatsRootOverride)
        {
            if (!string.IsNullOrWhiteSpace(memoryChatsRootOverride))
            {
                try
                {
                    return Path.GetFullPath(memoryChatsRootOverride.Trim());
                }
                catch
                {
                    return memoryChatsRootOverride.Trim();
                }
            }

            return Path.Combine(GetProductRoot(), "Memory", "Chats");
        }

        private static string GetProductRoot()
        {
            string bundleDir;
            try
            {
                var processPath = Environment.ProcessPath;
                bundleDir = !string.IsNullOrWhiteSpace(processPath)
                    ? (Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory)
                    : AppContext.BaseDirectory;
            }
            catch
            {
                bundleDir = AppContext.BaseDirectory;
            }

            if (Directory.Exists(Path.Combine(bundleDir, "Modules")) || Directory.Exists(Path.Combine(bundleDir, "Dock")))
                return bundleDir;

            var productDir = Path.Combine(bundleDir, "PRODUCT");
            if (Directory.Exists(Path.Combine(productDir, "Modules")) || Directory.Exists(Path.Combine(productDir, "Dock")))
                return productDir;

            var mainDir = Path.Combine(bundleDir, "MAIN");
            if (Directory.Exists(Path.Combine(mainDir, "Modules")))
            {
                var devProduct = Path.Combine(mainDir, "PRODUCT");
                return Directory.Exists(devProduct) ? devProduct : bundleDir;
            }

            return bundleDir;
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

                foreach (var entry in TruthReader.Read(path, repairTailFirst: false))
                {
                    var txt = NormalizeForStorage(entry.Payload);
                    if (string.IsNullOrWhiteSpace(txt)) continue;

                    idx.Seen.Add(Fingerprint(entry.Role, NormalizeForFingerprint(txt)));
                }
            }
            catch
            {
            }
            finally
            {
                idx.Loaded = true;
            }
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
    }
}
