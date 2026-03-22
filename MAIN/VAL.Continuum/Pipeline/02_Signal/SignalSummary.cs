using System;
using System.Collections.Generic;

namespace VAL.Continuum.Pipeline.Signal
{
    internal sealed class SignalSummary
    {
        public IReadOnlyList<string> PreviousChatSummary { get; init; } = Array.Empty<string>();
    }
}
