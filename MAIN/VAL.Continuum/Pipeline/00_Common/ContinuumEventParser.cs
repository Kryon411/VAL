namespace VAL.Continuum
{
    internal static class ContinuumEventParser
    {
        public static bool TryParseRefreshInjectSuccess(string value, out string mode, out string label)
        {
            mode = string.Empty;
            label = string.Empty;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            var parts = value.Split(':');
            if (parts.Length < 3)
                return false;

            mode = parts[1].Trim();
            label = parts[2].Trim();
            return !string.IsNullOrWhiteSpace(mode);
        }

        public static bool IsPulseCompletionTarget(string label)
        {
            return label.Equals("new_chat", StringComparison.OrdinalIgnoreCase) ||
                   label.Equals("new_chat_root", StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryParseSignalReplySettled(string value, out string assistantTurnId)
        {
            assistantTurnId = string.Empty;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            const string prefix = "signal.reply.settled:";
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            assistantTurnId = value[prefix.Length..].Trim();
            return !string.IsNullOrWhiteSpace(assistantTurnId);
        }
    }
}
