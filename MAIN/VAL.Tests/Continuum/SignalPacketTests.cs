using VAL.Continuum.Pipeline.Signal;
using Xunit;

namespace VAL.Tests.Continuum
{
    public sealed class SignalPacketTests
    {
        [Fact]
        public void TryParseAcceptsValidSemanticSummary()
        {
            var ok = SignalPacket.TryParse(BuildValidSignalSummary(), out var summary);

            Assert.True(ok);
            Assert.NotNull(summary);
            Assert.Equal("Continuum now owns final Pulse packet composition.", summary.PreviousChatSummary[0]);
            Assert.Equal("Rewire ContinuumHost to carry the frozen snapshot through Signal.", summary.OpenLoops[0]);
            Assert.Equal("Keep deterministic fallback intact if Signal fails.", summary.CriticalContext[0]);
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
@"PREVIOUS CHAT SUMMARY
Continuum owns the final packet.

OPEN LOOPS
- Rewire the host.

CRITICAL CONTEXT
- Keep fallback deterministic.";

            var ok = SignalPacket.TryParse(malformed, out _);

            Assert.False(ok);
        }

        private static string BuildValidSignalSummary()
        {
            return
@"PREVIOUS CHAT SUMMARY
- Continuum now owns final Pulse packet composition.
- Signal is narrowed to semantic summary output only.

OPEN LOOPS
- Rewire ContinuumHost to carry the frozen snapshot through Signal.
- Remove the old assistant-authored handoff path.

CRITICAL CONTEXT
- Keep deterministic fallback intact if Signal fails.
- Keep WWLO deterministic and separate from Truth Walkback.";
        }
    }
}
