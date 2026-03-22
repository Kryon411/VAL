using System;
using System.Collections.Generic;

namespace VAL.Continuum.Pipeline
{
    internal sealed class PulseExchangeBlock
    {
        public static PulseExchangeBlock Empty { get; } = new();

        public string Source { get; init; } = string.Empty;
        public string User { get; init; } = string.Empty;
        public string Assistant { get; init; } = string.Empty;
    }

    internal sealed class DeterministicPulseSections
    {
        public PulseExchangeBlock WhereWeLeftOff { get; init; } = PulseExchangeBlock.Empty;
        public IReadOnlyList<PulseExchangeBlock> TruthWalkbackHighlights { get; init; } = Array.Empty<PulseExchangeBlock>();
        public IReadOnlyList<string> OpenLoopFacts { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> CriticalFacts { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> ArtifactsAndReferences { get; init; } = Array.Empty<string>();
    }
}
