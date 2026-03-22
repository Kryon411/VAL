using VAL.Continuum.Pipeline.Signal;
using Xunit;

namespace VAL.Tests.Continuum
{
    public sealed class SignalPacketTests
    {
        [Fact]
        public void TryParseAcceptsValidPacket()
        {
            var ok = SignalPacket.TryParse(BuildValidSignalPacket(), out var packet);

            Assert.True(ok);
            Assert.NotNull(packet);
            Assert.Equal("Resolved implementation plan; ready for coding.", packet.CurrentState.Split('\n')[0].Substring("Status:".Length).Trim());
            Assert.Equal("User requested Pulse vNext implementation in fresh worktree.", packet.WhereWeLeftOff.User);
            Assert.Equal("Implement Pulse vNext with a narrow Signal stage and safe fallback.", packet.WhereWeLeftOff.Assistant);
        }

        [Fact]
        public void TryParseRejectsMissingCurrentStateValue()
        {
            var malformed = BuildValidSignalPacket().Replace(
                "Active objective: Implement Pulse vNext",
                "Active objective:");

            var ok = SignalPacket.TryParse(malformed, out _);

            Assert.False(ok);
        }

        [Fact]
        public void TryParseRejectsMissingWhereWeLeftOffAssistant()
        {
            var malformed = BuildValidSignalPacket().Replace(
                "\nASSISTANT:\nImplement Pulse vNext with a narrow Signal stage and safe fallback.\n",
                "\nImplement Pulse vNext with a narrow Signal stage and safe fallback.\n");

            var ok = SignalPacket.TryParse(malformed, out _);

            Assert.False(ok);
        }

        [Fact]
        public void TryParseRejectsConversationalPreface()
        {
            var malformed = "Here you go.\n\n" + BuildValidSignalPacket();

            var ok = SignalPacket.TryParse(malformed, out _);

            Assert.False(ok);
        }

        [Fact]
        public void TryRenderPulsePacketUsesTemplateAndPreservesAnchors()
        {
            var ok = SignalPacket.TryParse(BuildValidSignalPacket(), out var packet);
            Assert.True(ok);

            var rendered = SignalPacket.TryRenderPulsePacket(BuildTemplate(), packet, out var pulsePacket);

            Assert.True(rendered);
            Assert.Contains("PRIME DIRECTIVE (READ FIRST)", pulsePacket);
            Assert.Contains("WHERE WE LEFT OFF — LAST COMPLETE EXCHANGE (AUTHORITATIVE)", pulsePacket);
            Assert.Contains("USER:\nUser requested Pulse vNext implementation in fresh worktree.", pulsePacket);
            Assert.Contains("ASSISTANT:\nImplement Pulse vNext with a narrow Signal stage and safe fallback.", pulsePacket);
            Assert.Contains("CONTEXT FILLER (REFERENCE ONLY — DO NOT ADVANCE FROM HERE)", pulsePacket);
        }

        private static string BuildValidSignalPacket()
        {
            return
@"VAL Pulse Handoff

CURRENT STATE
Status: Resolved implementation plan; ready for coding.
Thread mode: Coding
Active objective: Implement Pulse vNext
Next expected assistant action: Make the approved code changes.
Last stable checkpoint: Fresh worktree created from synced Playground baseline.

TAIL CHECK
Recent turns operationally relevant: yes
Tail summary: Planning approved and implementation requested.
Tail decision: Resume from the approved Pulse vNext plan.

WHERE WE LEFT OFF — LAST COMPLETE EXCHANGE (AUTHORITATIVE)
Source: Current chat
USER:
User requested Pulse vNext implementation in fresh worktree.
ASSISTANT:
Implement Pulse vNext with a narrow Signal stage and safe fallback.

HOW TO PROCEED
- Begin from WHERE WE LEFT OFF.
- Treat the WWLO USER line as the active prompt if it is unresolved.
- If WWLO contains an unanswered direct instruction, answer it directly.
- Otherwise acknowledge readiness in one short line and wait.
- Treat all lower sections as reference only.

OPEN LOOPS
- Add the Signal stage without widening scope.

CRITICAL CONTEXT
- Preserve the legacy Pulse builder as fallback.

ARTIFACTS AND REFERENCES
- MAIN/Modules/Continuum/Signal.Prompt.v1.txt

ACTIVE THREAD (MOST RELEVANT PRIOR EXCHANGE)
Source: Planning chat
USER:
Pulse vNext planning is approved.
ASSISTANT:
Implementation can proceed within the approved boundary.

CONTEXT FILLER (REFERENCE ONLY — DO NOT ADVANCE FROM HERE)
- Phase 7 and warning cleanup are complete.

End of Pulse Handoff";
        }

        private static string BuildTemplate()
        {
            return
@"The following is a guide and carried context from a previous chat.

Use this authoritative snapshot of the preceding discussion to determine:
- what is active
- what is in scope
- where work should resume

Do not recreate, summarize, reinterpret, or advance earlier material unless explicitly instructed.

PRIME DIRECTIVE (READ FIRST)
On the first assistant reply after injection:
- If WHERE WE LEFT OFF contains an unanswered USER instruction, answer it directly.
- Otherwise acknowledge readiness in one short line and wait.

CURRENT STATE
Status:
Thread mode:
Active objective:
Next expected assistant action:
Last stable checkpoint:

TAIL CHECK
Recent turns operationally relevant:
Tail summary:
Tail decision:

WHERE WE LEFT OFF — LAST COMPLETE EXCHANGE (AUTHORITATIVE)
Source:
USER:
ASSISTANT:

HOW TO PROCEED
- Begin from WHERE WE LEFT OFF.
- Treat the WWLO USER line as the active prompt to answer in this chat.
- If that section contains a direct unresolved instruction, answer it.
- Do not restate or quote WWLO.
- If there is no unresolved instruction, acknowledge continuity in one short line and wait.
- Treat all other sections as reference only.

OPEN LOOPS
- 

CRITICAL CONTEXT
- 

ARTIFACTS AND REFERENCES
- 

ACTIVE THREAD (MOST RELEVANT PRIOR EXCHANGE)
Source:
USER:
ASSISTANT:

CONTEXT FILLER (REFERENCE ONLY — DO NOT ADVANCE FROM HERE)
- ";
        }
    }
}
