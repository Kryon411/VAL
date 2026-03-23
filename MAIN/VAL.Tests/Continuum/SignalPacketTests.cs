using VAL.Continuum.Pipeline.Signal;
using Xunit;

namespace VAL.Tests.Continuum
{
    public sealed class SignalPacketTests
    {
        [Fact]
        public void TryParseAcceptsValidPreviousChatSummary()
        {
            var ok = SignalPacket.TryParse(BuildValidSignalSummary(), out var summary);

            Assert.True(ok);
            Assert.NotNull(summary);
            Assert.Equal("Continuum now owns final Pulse packet composition.", summary.PreviousChatSummary[0]);
            Assert.Equal("Signal is narrowed to a visible THREAD STATE SUMMARY only.", summary.PreviousChatSummary[1]);
        }

        [Fact]
        public void TryParseAcceptsDomNormalizedParagraphSummary()
        {
            var ok = SignalPacket.TryParse(BuildDomNormalizedSignalSummary(), out var summary);

            Assert.True(ok);
            Assert.NotNull(summary);
            Assert.Equal(5, summary.PreviousChatSummary.Count);
            Assert.Equal("Host-side idempotent logging was implemented, eliminating duplication across restarts and stabilizing append-only behavior.", summary.PreviousChatSummary[0]);
            Assert.Equal("System has reached a near-complete state, with remaining work focused on refinement, ergonomics, and smoothing handoff behavior rather than core functionality.", summary.PreviousChatSummary[4]);
        }

        [Fact]
        public void TryParseStillAcceptsLegacyPreviousChatSummaryHeading()
        {
            var ok = SignalPacket.TryParse(BuildLegacySignalSummary(), out var summary);

            Assert.True(ok);
            Assert.NotNull(summary);
            Assert.Equal("Legacy packets remain readable while the new heading rolls out.", summary.PreviousChatSummary[0]);
        }

        [Fact]
        public void TryParseRejectsConversationalPreface()
        {
            var malformed = "Here you go.\n\n" + BuildValidSignalSummary();

            var ok = SignalPacket.TryParse(malformed, out _);

            Assert.False(ok);
        }

        [Fact]
        public void TryParseRejectsLegacyFullHandoffShape()
        {
            var malformed =
@"VAL Pulse Handoff

CURRENT STATE
Status: Legacy

OPEN LOOPS
- This should not parse.

End of Pulse Handoff";

            var ok = SignalPacket.TryParse(malformed, out _);

            Assert.False(ok);
        }

        [Fact]
        public void TryParseRejectsNonBulletSectionBody()
        {
            var malformed =
@"THREAD STATE SUMMARY
Continuum owns the final packet.";

            var ok = SignalPacket.TryParse(malformed, out _);

            Assert.False(ok);
        }

        [Fact]
        public void TryParseRejectsExtraSemanticHeadings()
        {
            var malformed =
@"THREAD STATE SUMMARY
- Continuum owns the final packet.

OPEN LOOPS
- Rewire the host.";

            var ok = SignalPacket.TryParse(malformed, out _);

            Assert.False(ok);
        }

        private static string BuildValidSignalSummary()
        {
            return
@"THREAD STATE SUMMARY
- Continuum now owns final Pulse packet composition.
- Signal is narrowed to a visible THREAD STATE SUMMARY only.";
        }

        private static string BuildDomNormalizedSignalSummary()
        {
            return
@"THREAD STATE SUMMARY

Host-side idempotent logging was implemented, eliminating duplication across restarts and stabilizing append-only behavior.

Backfill and capture are now deterministic and resumable, shifting remaining issues from correctness to performance and policy tuning.

Tagging noise in summaries was identified as a policy issue, leading to adoption of a stricter Context.txt to reduce over-tagging.

Quick refresh outputs confirm correct rehydration behavior, with assistant responses aligning to constraints and workflow without drift.

System has reached a near-complete state, with remaining work focused on refinement, ergonomics, and smoothing handoff behavior rather than core functionality.";
        }

        private static string BuildLegacySignalSummary()
        {
            return
@"PREVIOUS CHAT SUMMARY
- Legacy packets remain readable while the new heading rolls out.
- The parser should accept both headings during migration.";
        }
    }
}
