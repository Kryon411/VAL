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
    public static class TruthStorage
    {
        public const string TruthFileName = "Truth.log";

        private sealed class RebuildSession
        {
            public readonly object Gate = new object();
            public readonly System.Collections.Generic.HashSet<string> Seen =
                new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            public string TempPath = string.Empty;
            public CancellationToken Token;
            public string BackupPath = string.Empty;
        }

        // Truth.log must be boring infrastructure:
        // - append-only
        // - idempotent across chat switching + app restarts
        // - never dependent on client in-memory "seen" sets
        //
        // To achieve this without changing the on-disk line format, we de-dupe on the host
        // using a stable fingerprint of (role + normalized text), loaded from the existing
        // Truth.log on first write per chat.
        private sealed class ChatIndex
        {
            public readonly object Gate = new object();
            public readonly System.Collections.Generic.HashSet<string> Seen =
                new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            public bool Loaded;
        }

        private static readonly ConcurrentDictionary<string, ChatIndex> _indexByChat =
            new ConcurrentDictionary<string, ChatIndex>(StringComparer.Ordinal);

        private static readonly ConcurrentDictionary<string, RebuildSession> _rebuildByChat =
            new ConcurrentDictionary<string, RebuildSession>(StringComparer.Ordinal);

        public static bool TryBeginTruthRebuild(string chatId, bool backupExisting, out string backupPath, out string tempTruthPath, CancellationToken token)
        {
            backupPath = string.Empty;
            tempTruthPath = string.Empty;

            if (string.IsNullOrWhiteSpace(chatId)) return false;

            try
            {
                EnsureChatDir(chatId);

                // Ensure only one rebuild per chat.
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
                        // best-effort
                    }
                }

                tempTruthPath = Path.Combine(dir, TruthFileName + ".tmp");
                // Ensure we don't collide with any stale temp files.
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

                // Ensure the file exists so append operations are deterministic.
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

        public static void AbortTruthRebuild(string chatId)
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
                    catch { }
                }
            }
            catch { }
        }

        public static void AbortAllTruthRebuilds()
        {
            try
            {
                foreach (var kv in _rebuildByChat)
                {
                    try { AbortTruthRebuild(kv.Key); } catch { }
                }
            }
            catch { }
        }

        public static bool TryCommitTruthRebuild(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return false;

            try
            {
                if (!_rebuildByChat.TryRemove(chatId, out var session))
                    return false;

                if (session.Token.IsCancellationRequested)
                {
                    // Cancelled: do not touch the final file.
                    try { if (File.Exists(session.TempPath)) File.Delete(session.TempPath); } catch { }
                    return false;
                }

                var finalPath = GetTruthPath(chatId);
                if (string.IsNullOrWhiteSpace(session.TempPath) || !File.Exists(session.TempPath))
                    return false;

                // Atomic commit: temp -> final
                AtomicFile.ReplaceAtomic(session.TempPath, finalPath);

                // Reset host de-dupe index so future appends load from the new file.
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
                catch { }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetProductRoot()
        {
            // Prefer the real bundle directory to avoid .NET single-file extraction temp paths.
            string bundleDir;
            try
            {
                var p = Environment.ProcessPath;
                bundleDir = !string.IsNullOrWhiteSpace(p) ? (Path.GetDirectoryName(p) ?? AppContext.BaseDirectory) : AppContext.BaseDirectory;
            }
            catch
            {
                bundleDir = AppContext.BaseDirectory;
            }

            // Published layout: bundleDir IS PRODUCT (contains Modules/Dock)
            if (Directory.Exists(Path.Combine(bundleDir, "Modules")) || Directory.Exists(Path.Combine(bundleDir, "Dock")))
                return bundleDir;

            // If bundleDir is parent of PRODUCT
            var productDir = Path.Combine(bundleDir, "PRODUCT");
            if (Directory.Exists(Path.Combine(productDir, "Modules")) || Directory.Exists(Path.Combine(productDir, "Dock")))
                return productDir;

            // Dev layout: MAIN (Memory still written under MAIN\PRODUCT by convention if present)
            var mainDir = Path.Combine(bundleDir, "MAIN");
            if (Directory.Exists(Path.Combine(mainDir, "Modules")))
            {
                var devProduct = Path.Combine(mainDir, "PRODUCT");
                return Directory.Exists(devProduct) ? devProduct : bundleDir;
            }

            return bundleDir;
        }

        public static string GetChatDir(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                throw new ArgumentNullException(nameof(chatId));

            var root = GetProductRoot();

            // Default: <PRODUCT>\Memory\Chats\<chatId>
            return Path.Combine(root, "Memory", "Chats", chatId);
        }

        public static string GetTruthPath(string chatId)
        {
            return Path.Combine(GetChatDir(chatId), TruthFileName);
        }

        public static string EnsureChatDir(string chatId)
        {
            var dir = GetChatDir(chatId);
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }


        /// <summary>
        /// Chronicle/Recovery helper: resets (clears) the host in-memory de-dupe index for this chat,
        /// and optionally backs up + removes the existing Truth.log so it can be rebuilt cleanly.
        /// Must never throw.
        /// </summary>
        public static bool TryResetTruthLog(string chatId, bool backupExisting, out string backupPath)
        {
            backupPath = string.Empty;
            if (string.IsNullOrWhiteSpace(chatId)) return false;

            try { EnsureChatDir(chatId); } catch { }

            try
            {
                var idx = _indexByChat.GetOrAdd(chatId, _ => new ChatIndex());
                lock (idx.Gate)
                {
                    // Clear in-memory de-dupe state so a fresh Truth.log can be reconstructed.
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

        public static void AppendTruthLine(string chatId, char role, string text)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return;
            if (string.IsNullOrWhiteSpace(text)) return;

            try
            {
                EnsureChatDir(chatId);

                var roleChar = (role == 'A' || role == 'a') ? 'A' : 'U';
                var normalized = NormalizeForStorage(text);
                if (string.IsNullOrWhiteSpace(normalized)) return;

                bool appended = false;

                // If Chronicle is rebuilding this chat, route writes to the rebuild temp file
                // and commit atomically only when Chronicle completes.
                if (_rebuildByChat.TryGetValue(chatId, out var rebuild))
                {
                    if (rebuild.Token.IsCancellationRequested)
                        return;

                    lock (rebuild.Gate)
                    {
                        if (rebuild.Token.IsCancellationRequested)
                            return;

                        var fp = Fingerprint(roleChar, NormalizeForFingerprint(normalized));
                        if (rebuild.Seen.Contains(fp))
                            return;

                        var prefix = roleChar == 'A' ? "A|" : "U|";
                        var line = prefix + normalized + Environment.NewLine;
                        File.AppendAllText(rebuild.TempPath, line);
                        rebuild.Seen.Add(fp);
                        appended = true;
                    }
                }
                else
                {
                    var idx = _indexByChat.GetOrAdd(chatId, _ => new ChatIndex());
                    lock (idx.Gate)
                    {
                        // If Truth.log was deleted out-of-band (rebuild scripts, manual cleanup),
                        // clear the in-memory de-dupe index so capture can resume safely.
                        try
                        {
                            var path = GetTruthPath(chatId);
                            if (idx.Loaded && !File.Exists(path) && idx.Seen.Count > 0)
                                idx.Seen.Clear();
                        }
                        catch { }

                        EnsureIndexLoaded(chatId, idx);

                        var fp = Fingerprint(roleChar, NormalizeForFingerprint(normalized));
                        if (idx.Seen.Contains(fp))
                            return; // idempotent

                        var prefix = roleChar == 'A' ? "A|" : "U|";
                        // One physical line per message (payload itself is already escaped for storage).
                        var line = prefix + normalized + Environment.NewLine;

                        // IMPORTANT: only mark as "seen" after the append succeeds,
                        // otherwise an IO failure could cause permanent message loss.
                        if (!AtomicFile.TryAppendAllText(GetTruthPath(chatId), line))
                            return;
                        idx.Seen.Add(fp);

                        appended = true;
                    }
                }

                // Telemetry: best-effort size hygiene (nudge Pulse before sessions get unstable).
                // Must never affect Truth writing or throw.
                if (appended)
                {
                    long bytes = 0;
                    try
                    {
                        var p = GetTruthPath(chatId);
                        if (File.Exists(p)) bytes = new FileInfo(p).Length;
                    }
                    catch { }

                    TryUpdateTelemetry(chatId, bytes);
                }
            }
            catch
            {
                // must never crash app
            }
        }

        private static void TryUpdateTelemetry(string chatId, long bytes)
        {
            try
            {
                // We keep this loosely coupled: if Telemetry is removed from a build, Truth still compiles and runs.
                var t = Type.GetType("VAL.Continuum.Pipeline.Telemetry.TelemetryThresholdMonitor, VAL");
                var m = t?.GetMethod("UpdateFromTruthBytes");
                if (m != null)
                    m.Invoke(null, new object?[] { chatId, bytes });
            }
            catch
            {
                // telemetry must never throw
            }
        }

        private static string NormalizeForStorage(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            // 1) Normalize real newline characters.
            // Truth.log stores each payload on a single physical line, so real newlines must be escaped.
            s = s.Replace("\r\n", "\n").Replace("\r", "\n");

            // 2) Strip UI chrome *before* fingerprinting/appending so host idempotency
            // survives restart / recapture variants (e.g. "ChatGPT said:").
            // This is structural-only cleanup (not summarization, not selection).
            s = StripUiChromeForStorage(s);

            // 3) Ensure the payload cannot create multi-line writes.
            // Only replace REAL newline characters; already-escaped "\\n" sequences remain.
            s = s.Replace("\n", "\\n");

            // 4) Keep internal spacing as-is; just trim edge noise.
            return s.Trim();
        }
        // Fingerprint normalization is allowed to be slightly more aggressive than storage normalization.
        // We keep Truth.log payloads readable-ish, but we want host idempotency even when the UI introduces
        // minor formatting variance (extra blank lines, spacing differences, etc.).
        private static string NormalizeForFingerprint(string storageNormalized)
        {
            if (string.IsNullOrEmpty(storageNormalized)) return string.Empty;

            try
            {
                var s = storageNormalized;

                // Unify escaped newline spellings (seen in some capture variants).
                s = s.Replace("\\r\\n", "\\n").Replace("\\r", "\\n");

                // Collapse runs of escaped newlines to ONE token so "\n" and "\n\n" de-dupe.
                s = Regex.Replace(s, @"(\\n){2,}", "\\n");

                // Collapse runs of spaces/tabs.
                s = Regex.Replace(s, @"[ \t]{2,}", " ");

                return s.Trim();
            }
            catch
            {
                // Never let fingerprinting throw.
                return (storageNormalized ?? string.Empty).Trim();
            }
        }



        private static string StripUiChromeForStorage(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            // Common capture glue from the ChatGPT UI (seen in both real-newline and escaped-newline forms).
            // These tokens are not part of the user's/assistant's message content and should never affect
            // fingerprinting.

            // "Copy code" blocks
            s = s.Replace("text\nCopy code\n", string.Empty);
            s = s.Replace("text\\nCopy code\\n", string.Empty);
            s = s.Replace("Copy code\n", string.Empty);
            s = s.Replace("Copy code\\n", string.Empty);
            s = s.Replace("\nCopy code", string.Empty);
            s = s.Replace("\\nCopy code", string.Empty);

            // "Document" wrappers
            s = s.Replace("\nDocument\n", "\n");
            s = s.Replace("\\nDocument\\n", "\\n");

            // Normalize away the most common leading wrapper: "ChatGPT said:".
            // IMPORTANT: only strip it when it is a leading prefix (avoid nuking legitimate mentions).
            var t = s.TrimStart();
            if (t.StartsWith("ChatGPT said:", StringComparison.OrdinalIgnoreCase))
            {
                t = t.Substring("ChatGPT said:".Length);
            }
            else if (t.StartsWith("ChatGPT said", StringComparison.OrdinalIgnoreCase))
            {
                t = t.Substring("ChatGPT said".Length);
                // Handle the rare "ChatGPT said :" capture.
                t = t.TrimStart();
                if (t.StartsWith(':'))
                    t = t.Substring(1);
            }

            // Fix a known glue artifact where the capture concatenates "text" with your product name.
            t = t.TrimStart();
            if (t.StartsWith("textVAL ", StringComparison.OrdinalIgnoreCase))
                t = t.Substring(4);

            return t;
        }

        private static string Fingerprint(char roleChar, string normalizedText)
        {
            // Stable across processes/runs.
            var bytes = Encoding.UTF8.GetBytes(roleChar + "|" + normalizedText);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }

        private static void EnsureIndexLoaded(string chatId, ChatIndex idx)
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
                        // best-effort logging
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
                // If we fail to load for any reason, we still mark loaded to avoid
                // re-trying (and potentially deadlocking) on every append.
            }
            finally
            {
                idx.Loaded = true;
            }
        }

        public static string ReadTruthText(string chatId)
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

        public static string[] ReadTruthLines(string chatId)
        {
            var path = GetTruthPath(chatId);
            if (!File.Exists(path))
                return Array.Empty<string>();

            var lines = new System.Collections.Generic.List<string>();
            foreach (var entry in TruthReader.Read(path, repairTailFirst: true))
                lines.Add($"{entry.Role}|{entry.Payload}");

            return lines.ToArray();
        }
    }
}
