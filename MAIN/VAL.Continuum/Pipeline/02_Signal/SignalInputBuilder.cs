using System;
namespace VAL.Continuum.Pipeline.Signal
{
    internal static class SignalInputBuilder
    {
        internal static string Build(string promptInstructions)
        {
            var instructions = Normalize(promptInstructions);
            return string.IsNullOrWhiteSpace(instructions) ? string.Empty : instructions;
        }

        private static string Normalize(string text)
            => (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }
}
