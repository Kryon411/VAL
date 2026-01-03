Continuum — Filter 1 / Filter 2 Pipeline (Foundation)

What this adds
- Pipeline/03_Filter1: TruthView -> Seed.log (filtered + sliced)
- Pipeline/04_Filter2: Seed exchanges -> RestructuredSeed.log (reverse-order pack, budgeted)

Pulse wiring
- Pipeline/01_QuickRefresh/QuickRefresh.Flow.cs now runs:
  TruthNormalize.BuildView(chatId)
    -> Filter1BuildSeed.BuildSeed(truth)      (writes Seed.log beside Truth.log)
    -> Filter2Restructure.BuildRestructuredSeed(seed.Exchanges) (writes RestructuredSeed.log beside Truth.log)
    -> EssenceBuild.BuildEssenceM(chatId, restructured)
    -> ContinuumPreamble.InjectIntoEssence(...)
    -> EssenceInjectQueue.Enqueue(...)

Knobs (edit these constants)
- Pipeline/03_Filter1/Filter1.Rules.cs
  AssistantSliceTotalChars (default 1500)
  AssistantSentenceOverflowMaxChars
  UserSliceMaxChars
  MaxExchangeChars

- Pipeline/04_Filter2/Filter2.Rules.cs
  BudgetChars (default 28000)
  WhereWeLeftOffCount
  OverflowFinishExchangeMaxChars

Audit outputs (per chat session folder)
- Seed.log
- RestructuredSeed.log
- Essence-M.Pulse.txt (final injected payload)

Notes
- Truth.Normalize.cs now sets TruthMessage.LineIndex (0-based) so Seed/Restructured can cite TruthLine locators.


Chronicle (Truth backfill / rebuild)
- Control Centre → Chronicle starts a deterministic UI walk (top→bottom) to rebuild Truth.log when it is missing (e.g., after a rebuild script deletes logs).
- Host resets Truth.log + in-memory de-dupe index first, then the client auto-scrolls while capture is temporarily frozen to avoid out-of-order writes.
- Outputs: Chronicle.audit.txt (per session folder) records start/end + backup path.
