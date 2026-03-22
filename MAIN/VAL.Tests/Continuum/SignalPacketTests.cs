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
            Assert.Equal("Signal is narrowed to a visible PREVIOUS CHAT SUMMARY only.", summary.PreviousChatSummary[1]);
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
Continuum owns the final packet.";

            var ok = SignalPacket.TryParse(malformed, out _);

            Assert.False(ok);
        }

        [Fact]
        public void TryParseRejectsExtraSemanticHeadings()
        {
            var malformed =
@"PREVIOUS CHAT SUMMARY
- Continuum owns the final packet.

OPEN LOOPS
- Rewire the host.";

            var ok = SignalPacket.TryParse(malformed, out _);

            Assert.False(ok);
        }

        private static string BuildValidSignalSummary()
        {
            return
@"PREVIOUS CHAT SUMMARY
- Continuum now owns final Pulse packet composition.
- Signal is narrowed to a visible PREVIOUS CHAT SUMMARY only.";
        }
    }
}
