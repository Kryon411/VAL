using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using VAL.Continuum.Pipeline.Truth;

namespace VAL.Continuum.Pipeline.Filter1
{
    /// <summary>
    /// Filter 1: scans normalized TruthView and produces:
    /// - a list of exchanges (User -> Assistant)
    /// - a Seed.log text projection (filtered + sliced)
    ///
    /// Notes:
    /// - Deterministic: no semantic inference.
    /// - If the chat has unpaired turns (e.g., user without assistant yet), we still emit what exists.
    /// </summary>
    public static class Filter1BuildSeed
    {
        public sealed class SeedExchange
        {
            public int Index { get; init; }

            public string UserText { get; init; } = string.Empty;
            public string AssistantText { get; init; } = string.Empty;

            public int UserLineIndex { get; init; } = -1;
            public int AssistantLineIndex { get; init; } = -1;
        }

        public sealed class SeedResult
        {
            public IReadOnlyList<SeedExchange> Exchanges { get; init; } = Array.Empty<SeedExchange>();
            public string SeedLogText { get; init; } = string.Empty;
        }

        public static SeedResult BuildSeed(TruthView truth)
        {
            if (truth == null) throw new ArgumentNullException(nameof(truth));

            var all = truth.Messages ?? Array.Empty<TruthMessage>();
            if (all.Count == 0)
            {
                return new SeedResult
                {
                    Exchanges = Array.Empty<SeedExchange>(),
                    SeedLogText = string.Empty
                };
            }

            var exchanges = PairIntoExchanges(all);

            var sb = new StringBuilder();
            foreach (var ex in exchanges)
            {
                // Render filtered projection (Seed.log)
                AppendSeedExchange(sb, ex);
                sb.AppendLine();
                sb.AppendLine();
            }

            return new SeedResult
            {
                Exchanges = exchanges,
                SeedLogText = sb.ToString().Trim()
            };
        }

        
        private static List<SeedExchange> PairIntoExchanges(IReadOnlyList<TruthMessage> messages)
        {
            var exchanges = new List<SeedExchange>();

            var userSb = new StringBuilder();
            string assistantText = string.Empty;
            int userLine = -1;
            int assistantLine = -1;

            bool hasUser = false;
            bool hasAssistant = false;

            // Filter1 must be resilient to Truth.log anomalies:
            // - partial assistant captures (streaming) that appear as multiple assistant lines in a row
            // - replay / recapture blocks where old assistant turns get appended again
            //
            // We keep ONE assistant message per exchange and treat same-role repeats as "updates" only
            // if they look like the same message (shared prefix after whitespace compaction).
            var seenAssistant = new HashSet<string>(StringComparer.Ordinal);

            string LooseFingerprint(string role, string text)
            {
                // Collapse all whitespace to single spaces so blank-line variance de-dupes.
                if (string.IsNullOrEmpty(text)) return role + "|";

                var sb = new StringBuilder(Math.Min(text.Length, 4096));
                bool inWs = false;

                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (char.IsWhiteSpace(c))
                    {
                        if (!inWs)
                        {
                            sb.Append(' ');
                            inWs = true;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                        inWs = false;
                    }

                    if (sb.Length >= 4096)
                        break;
                }

                var compact = sb.ToString().Trim();

                try
                {
                    using var sha = SHA256.Create();
                    var bytes = Encoding.UTF8.GetBytes(role + "|" + compact);
                    var hash = sha.ComputeHash(bytes);
                    return Convert.ToHexString(hash);
                }
                catch
                {
                    return role + "|" + compact;
                }
            }

            string CompactForUpdate(string text)
            {
                if (string.IsNullOrEmpty(text)) return string.Empty;

                var sb = new StringBuilder(Math.Min(text.Length, 512));
                bool inWs = false;

                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (char.IsWhiteSpace(c))
                    {
                        if (!inWs)
                        {
                            sb.Append(' ');
                            inWs = true;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                        inWs = false;
                    }

                    if (sb.Length >= 512)
                        break;
                }

                return sb.ToString().Trim();
            }

            bool LooksLikeAssistantUpdate(string current, string candidate)
            {
                if (string.IsNullOrWhiteSpace(candidate)) return false;
                if (string.IsNullOrWhiteSpace(current)) return true;

                var a = CompactForUpdate(current);
                var b = CompactForUpdate(candidate);

                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return true;

                if (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal))
                    return true;

                int max = Math.Min(200, Math.Min(a.Length, b.Length));
                int common = 0;

                for (int i = 0; i < max; i++)
                {
                    if (a[i] != b[i]) break;
                    common++;
                }

                // Require a meaningful shared prefix so unrelated replay turns don't get glued together.
                return common >= 60 || (max > 0 && common >= (int)(max * 0.6));
            }

            void Reset()
            {
                userSb.Clear();
                assistantText = string.Empty;
                userLine = -1;
                assistantLine = -1;
                hasUser = false;
                hasAssistant = false;
            }

            void FinalizeExchange()
            {
                if (!hasUser && !hasAssistant)
                    return;

                var uRaw = hasUser ? userSb.ToString() : string.Empty;
                var aRaw = hasAssistant ? assistantText : string.Empty;

                var u = Filter1Rules.FilterUser(uRaw);
                var a = Filter1Rules.FilterAssistant(aRaw);

                // Protect Filter2 budget from pathological paste cases.
                if ((u?.Length ?? 0) + (a?.Length ?? 0) > Filter1Rules.MaxExchangeChars)
                {
                    u = "[USER CONTENT OMITTED: oversized exchange]";
                    a = "[ASSISTANT CONTENT OMITTED: oversized exchange]";
                }

                exchanges.Add(new SeedExchange
                {
                    Index = exchanges.Count + 1,
                    UserLineIndex = userLine,
                    AssistantLineIndex = assistantLine,
                    UserText = u ?? string.Empty,
                    AssistantText = a ?? string.Empty
                });

                Reset();
            }

            foreach (var m in messages)
            {
                if (m == null) continue;

                var role = m.Role;
                var text = (m.Text ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (role == TruthRole.Assistant)
                {
                    var fp = LooseFingerprint("A", text);

                    // If we're about to accept an assistant message as the FIRST assistant for the current exchange,
                    // but we've already seen this assistant content earlier in the chat, treat it as a replay and skip it.
                    if (hasUser && !hasAssistant && seenAssistant.Contains(fp))
                        continue;

                    if (!hasUser && !hasAssistant)
                    {
                        // Assistant-only (rare): start an exchange with empty user (still useful for continuity).
                        hasAssistant = true;
                        assistantLine = m.LineIndex;
                        assistantText = text;
                        seenAssistant.Add(fp);
                        continue;
                    }

                    if (!hasAssistant)
                    {
                        hasAssistant = true;
                        assistantLine = m.LineIndex;
                        assistantText = text;
                        seenAssistant.Add(fp);
                    }
                    else
                    {
                        // Consecutive assistant messages: only treat as streaming update if it resembles the current message.
                        if (LooksLikeAssistantUpdate(assistantText, text))
                        {
                            if (text.Length >= assistantText.Length)
                            {
                                assistantText = text;
                                assistantLine = m.LineIndex;
                                seenAssistant.Add(fp);
                            }
                        }
                        else
                        {
                            // Unrelated assistant turn (likely recapture/replay) -> ignore.
                            continue;
                        }
                    }
                }
                else // User OR Unknown (treat unknown as user intent authority)
                {
                    if (hasAssistant)
                    {
                        // New user message after assistant -> close previous exchange.
                        FinalizeExchange();
                    }

                    if (!hasUser)
                    {
                        hasUser = true;
                        userLine = m.LineIndex;
                        userSb.Append(text);
                    }
                    else
                    {
                        // Consecutive user messages -> join them into one "user" side.
                        userSb.AppendLine();
                        userSb.Append(text);
                    }
                }
            }

            FinalizeExchange();
            return exchanges;
        }



        private static void AppendSeedExchange(StringBuilder sb, SeedExchange ex)
        {
            if (sb == null) return;

            var msgLabel = $"***Message {ex.Index} - USER***";
            if (ex.UserLineIndex >= 0) msgLabel += $" (Source: Truth {ex.UserLineIndex}\u2013{ex.UserLineIndex})";
            msgLabel += ":";

            sb.AppendLine(msgLabel);

            if (!string.IsNullOrWhiteSpace(ex.UserText))
                sb.AppendLine(ex.UserText.Trim());
            else
                sb.AppendLine("[USER: empty]");

            sb.AppendLine();

            var respLabel = $"***Response {ex.Index} - ASSISTANT***";
            if (ex.AssistantLineIndex >= 0) respLabel += $" (Source: Truth {ex.AssistantLineIndex}\u2013{ex.AssistantLineIndex})";
            respLabel += ":";

            sb.AppendLine(respLabel);

            if (!string.IsNullOrWhiteSpace(ex.AssistantText))
                sb.AppendLine(ex.AssistantText.Trim());
            else
                sb.AppendLine("[ASSISTANT: empty]");
        }
    }
}
