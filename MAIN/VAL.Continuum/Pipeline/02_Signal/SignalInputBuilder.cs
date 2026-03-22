using System;
using System.Text;
using VAL.Continuum.Pipeline.Filter2;

namespace VAL.Continuum.Pipeline.Signal
{
    internal static class SignalInputBuilder
    {
        internal static string Build(string promptInstructions, PulseSnapshot snapshot, DeterministicPulseSections deterministicSections)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(deterministicSections);

            var instructions = Normalize(promptInstructions);
            if (string.IsNullOrWhiteSpace(instructions))
                return string.Empty;

            var sb = new StringBuilder();
            AppendBlock(sb, instructions);
            AppendSection(
                sb,
                "FROZEN PULSE SNAPSHOT",
                $"ChatId: {snapshot.ChatId}\nFrozen Truth Boundary: Truth <= {snapshot.FrozenBoundaryLineIndex}\nCaptured Messages: {snapshot.FrozenMessageCount}\nCaptured Exchanges: {snapshot.Filter1Exchanges.Count}");
            AppendSection(sb, "DETERMINISTIC SOURCE MATERIAL", Filter2Restructure.RenderDeterministicSections(deterministicSections));
            return sb.ToString().Trim();
        }

        private static void AppendSection(StringBuilder sb, string heading, string body)
        {
            var normalized = Normalize(body);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            if (sb.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine();
            }

            sb.AppendLine(heading);
            sb.Append(normalized);
        }

        private static void AppendBlock(StringBuilder sb, string body)
        {
            var normalized = Normalize(body);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            if (sb.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine();
            }

            sb.Append(normalized);
        }

        private static string Normalize(string text)
            => (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }
}
