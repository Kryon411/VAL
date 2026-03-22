using System;

namespace VAL.Continuum.Pipeline.Filter2
{
    /// <summary>
    /// Filter 2 rules (Seed.log -> RestructuredSeed).
    ///
    /// Design intent:
    /// - Reverse-order packing with a character budget (default 28k).
    /// - Always pin "WHERE WE LEFT OFF" (last 2 exchanges) at the top.
    /// - Deterministic, no semantic summarization.
    /// </summary>
    internal static class Filter2Rules
    {
        // Total budget for RestructuredSeed (does NOT include Context.txt preamble injected later).
        public const int BudgetChars = 28_000;

        // Allow finishing ONE whole exchange after budget is crossed (prevents half-turn cutoffs).
        public const int OverflowFinishExchangeMaxChars = 8_000;

        // Exchanges to pin at the top as the "handoff tail anchor" (latest exchange is authoritative).
        public const int WhereWeLeftOffCount = 2;

        // WWLO prefers uncut text only when safely bounded.
        public const int WhereWeLeftOffMaxTextChars = 6_000;

        // Keep supporting walkback compact so the final Pulse packet is materially smaller than the nested vNext handoff.
        public const int TruthWalkbackMaxExchanges = 4;

        // Deterministic open-loop / critical-context extraction should stay compact and readable.
        public const int DeterministicFactMaxItems = 8;
        public const int DeterministicFactMaxChars = 280;
    }
}
