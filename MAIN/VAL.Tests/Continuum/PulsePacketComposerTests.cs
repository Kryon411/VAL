using System;
using VAL.Continuum.Pipeline;
using VAL.Continuum.Pipeline.Filter2;
using VAL.Continuum.Pipeline.Signal;
using VAL.Continuum.Pipeline.Truth;
using Xunit;

namespace VAL.Tests.Continuum
{
    public sealed class PulsePacketComposerTests
    {
        [Fact]
        public void ComposeUsesLocalFinalPacketShapeWithoutNestedHandoff()
        {
            var snapshot = BuildSnapshot();
            var sections = Filter2Restructure.BuildSections(snapshot);
            var signalSummary = BuildSignalSummary();

            var rendered = PulsePacketComposer.Compose(snapshot, sections, signalSummary);

            Assert.Contains(PulsePacketComposer.PreviousChatSummaryHeading, rendered);
            Assert.Contains(PulsePacketComposer.WhereWeLeftOffHeading, rendered);
            Assert.Contains(PulsePacketComposer.OpenLoopsHeading, rendered);
            Assert.Contains(PulsePacketComposer.CriticalContextHeading, rendered);
            Assert.Contains(PulsePacketComposer.TruthWalkbackHighlightsHeading, rendered);
            Assert.Equal(1, CountHeadingLines(rendered, PulsePacketComposer.WhereWeLeftOffHeading));
            Assert.DoesNotContain("VAL Pulse Handoff", rendered);
            Assert.DoesNotContain("CURRENT STATE", rendered);
            Assert.DoesNotContain("TAIL CHECK", rendered);
            Assert.DoesNotContain("HOW TO PROCEED", rendered);
        }

        [Fact]
        public void ComposeKeepsWwloIdenticalWhenSignalIsMissing()
        {
            var snapshot = BuildSnapshot();
            var sections = Filter2Restructure.BuildSections(snapshot);

            var withSignal = PulsePacketComposer.Compose(snapshot, sections, BuildSignalSummary());
            var withoutSignal = PulsePacketComposer.Compose(snapshot, sections, signalSummary: null);

            Assert.Equal(
                ExtractSection(withSignal, PulsePacketComposer.WhereWeLeftOffHeading, PulsePacketComposer.OpenLoopsHeading),
                ExtractSection(withoutSignal, PulsePacketComposer.WhereWeLeftOffHeading, PulsePacketComposer.OpenLoopsHeading));
        }

        [Fact]
        public void ComposeFallbackStillProducesAValidPacket()
        {
            var snapshot = BuildSnapshot();
            var sections = Filter2Restructure.BuildSections(snapshot);

            var rendered = PulsePacketComposer.Compose(snapshot, sections, signalSummary: null);

            Assert.Contains("The following is a guide and carried context from a previous chat.", rendered);
            Assert.Contains("PREVIOUS CHAT SUMMARY", rendered);
            Assert.Contains("WHERE WE LEFT OFF", rendered);
            Assert.Contains("USER:", rendered);
            Assert.Contains("ASSISTANT:", rendered);
            Assert.Contains("TRUTH WALKBACK HIGHLIGHTS", rendered);
        }

        [Fact]
        public void ComposeOutputIsSmallerThanRepresentativeLegacyNestedPacket()
        {
            var snapshot = BuildSnapshot();
            var sections = Filter2Restructure.BuildSections(snapshot);
            var rendered = PulsePacketComposer.Compose(snapshot, sections, BuildSignalSummary());

            Assert.True(rendered.Length < BuildRepresentativeLegacyNestedPacket().Length);
        }

        private static PulseSnapshot BuildSnapshot()
        {
            var truth = new TruthView
            {
                ChatId = "chat-1",
                Messages = new[]
                {
                    new TruthMessage
                    {
                        Role = TruthRole.User,
                        LineIndex = 0,
                        Text = "We need Continuum to own final Pulse packet rendering."
                    },
                    new TruthMessage
                    {
                        Role = TruthRole.Assistant,
                        LineIndex = 1,
                        Text = "We'll move the final packet shape into Continuum and keep Signal focused on semantic output."
                    },
                    new TruthMessage
                    {
                        Role = TruthRole.User,
                        LineIndex = 2,
                        Text = "Task: Refactor VAL Pulse so Continuum owns the final handoff packet.\n- Preserve deterministic fallback if Signal fails.\n- Keep WWLO deterministic and separate.\n- Use C:\\Users\\Vault\\OneDrive\\Desktop\\VAL\\MAIN\\VAL.Continuum\\Pipeline\\00_Common\\ContinuumHost.cs\n- Use C:\\Users\\Vault\\OneDrive\\Desktop\\VAL\\MAIN\\VAL.Continuum\\Pipeline\\02_Signal\\SignalPacket.cs"
                    },
                    new TruthMessage
                    {
                        Role = TruthRole.Assistant,
                        LineIndex = 3,
                        Text = "Proceed by adding a frozen PulseSnapshot, deterministic sections, and a local PulsePacketComposer."
                    },
                    new TruthMessage
                    {
                        Role = TruthRole.User,
                        LineIndex = 4,
                        Text = "Keep the final payload plain text first, preserve stable headings, and keep the injection runtime unchanged."
                    },
                    new TruthMessage
                    {
                        Role = TruthRole.Assistant,
                        LineIndex = 5,
                        Text = "The host can build a deterministic fallback packet first, then ask Signal only for semantic summary bullets."
                    }
                }
            };

            return PulseSnapshot.Freeze("chat-1", truth, frozenBoundaryLineIndex: 5);
        }

        private static SignalSummary BuildSignalSummary()
        {
            return new SignalSummary
            {
                PreviousChatSummary = new[]
                {
                    "Continuum now owns final Pulse packet composition.",
                    "Signal is narrowed to semantic summary output only."
                },
                OpenLoops = new[]
                {
                    "Rewire ContinuumHost to carry the frozen snapshot through Signal."
                },
                CriticalContext = new[]
                {
                    "Keep deterministic fallback intact if Signal fails.",
                    "Do not let Pulse orchestration become the WWLO anchor."
                }
            };
        }

        private static int CountHeadingLines(string text, string heading)
        {
            int count = 0;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.Equals(lines[i].Trim(), heading, StringComparison.Ordinal))
                    count++;
            }

            return count;
        }

        private static string ExtractSection(string text, string heading, string nextHeading)
        {
            var start = text.IndexOf(heading, StringComparison.Ordinal);
            Assert.True(start >= 0);

            var next = text.IndexOf(nextHeading, start + heading.Length, StringComparison.Ordinal);
            Assert.True(next > start);

            return text.Substring(start, next - start).Trim();
        }

        private static string BuildRepresentativeLegacyNestedPacket()
        {
            return
@"The following is a guide and carried context from a previous chat.

VAL Pulse Handoff

PRIME DIRECTIVE (READ FIRST)
On the first assistant reply after injection:
- If WHERE WE LEFT OFF contains an unanswered USER instruction, answer it directly.
- Otherwise acknowledge readiness in one short line and wait.

CURRENT STATE
Status: Continuum refactor in progress.
Thread mode: Coding
Active objective: Move final Pulse rendering into Continuum.
Next expected assistant action: Rewire the host and parser.
Last stable checkpoint: Snapshot model and deterministic sections are defined.

TAIL CHECK
Recent turns operationally relevant: yes
Tail summary: Pulse ownership is moving local.
Tail decision: Continue from the approved refactor plan.

WHERE WE LEFT OFF — LAST COMPLETE EXCHANGE (AUTHORITATIVE)
Source: Truth 4-5
USER:
Keep the final payload plain text first, preserve stable headings, and keep the injection runtime unchanged.
ASSISTANT:
The host can build a deterministic fallback packet first, then ask Signal only for semantic summary bullets.

HOW TO PROCEED
- Begin from WHERE WE LEFT OFF.
- Treat all lower sections as reference only.

OPEN LOOPS
- Rewire ContinuumHost to carry the frozen snapshot through Signal.

CRITICAL CONTEXT
- Keep deterministic fallback intact if Signal fails.

ARTIFACTS AND REFERENCES
- C:\\Users\\Vault\\OneDrive\\Desktop\\VAL\\MAIN\\VAL.Continuum\\Pipeline\\00_Common\\ContinuumHost.cs
- C:\\Users\\Vault\\OneDrive\\Desktop\\VAL\\MAIN\\VAL.Continuum\\Pipeline\\02_Signal\\SignalPacket.cs

ACTIVE THREAD (MOST RELEVANT PRIOR EXCHANGE)
Source: Truth 2-3
USER:
Task: Refactor VAL Pulse so Continuum owns the final handoff packet.
ASSISTANT:
Proceed by adding a frozen PulseSnapshot, deterministic sections, and a local PulsePacketComposer.
- Preserve deterministic fallback if Signal fails.
- Keep WWLO deterministic and separate from Truth Walkback.
- Do not redesign the web injection runtime.

CONTEXT FILLER (REFERENCE ONLY — DO NOT ADVANCE FROM HERE)
- Preserve deterministic fallback if Signal fails.
- Keep WWLO deterministic and separate.
- Final packet must stay plain-text first, readable in the composer and after send.
- Stable headings and USER:/ASSISTANT: anchors should remain where needed.
- Pulse orchestration exchanges must be excluded from WWLO candidate selection.
- Remove the old assistant-authored handoff structure after the local composer is in place.

ARTIFACTS AND REFERENCES (SECONDARY)
- MAIN\\Modules\\Continuum\\Signal.Prompt.v1.txt
- MAIN\\Modules\\Continuum\\Pulse.Packet.Template.vNext.txt
- C:\\Users\\Vault\\OneDrive\\Desktop\\VAL\\MAIN\\Modules\\Continuum\\Client\\Continuum.Client.js

End of Pulse Handoff";
        }
    }
}
