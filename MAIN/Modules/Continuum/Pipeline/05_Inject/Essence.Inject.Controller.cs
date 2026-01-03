using System;

namespace VAL.Continuum.Pipeline.Inject
{
    /// <summary>
    /// Minimal seed model for vNext Pulse injection.
    /// (Pulse-only, Essence-M only.)
    /// </summary>
    public static class EssenceInjectController
    {
        public sealed class InjectSeed
        {
            public string ChatId { get; set; } = string.Empty;
            public string Mode { get; set; } = "Pulse";
            public string EssenceText { get; set; } = string.Empty;

            public bool OpenNewChat { get; set; } = true;

            public string SourceFileName { get; set; } = string.Empty;
            public string EssenceFileName { get; set; } = string.Empty;
        }
    }
}
