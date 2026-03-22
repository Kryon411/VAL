using VAL.Continuum.Pipeline;
using VAL.Continuum.Pipeline.Filter2;
using VAL.Continuum.Pipeline.Truth;
using Xunit;

namespace VAL.Tests.Continuum
{
    public sealed class PulseSnapshotTests
    {
        [Fact]
        public void FreezeExcludesPulseOrchestrationTurnsFromWwloSelection()
        {
            var truth = new TruthView
            {
                ChatId = "chat-2",
                Messages = new[]
                {
                    new TruthMessage
                    {
                        Role = TruthRole.User,
                        LineIndex = 0,
                        Text = "Review the Continuum refactor plan."
                    },
                    new TruthMessage
                    {
                        Role = TruthRole.Assistant,
                        LineIndex = 1,
                        Text = "The plan is approved and ready for implementation."
                    },
                    new TruthMessage
                    {
                        Role = TruthRole.User,
                        LineIndex = 2,
                        Text = "Refactor VAL Pulse so Continuum owns final packet rendering."
                    },
                    new TruthMessage
                    {
                        Role = TruthRole.Assistant,
                        LineIndex = 3,
                        Text = "Start with a frozen PulseSnapshot and a local packet composer."
                    },
                    new TruthMessage
                    {
                        Role = TruthRole.User,
                        LineIndex = 4,
                        Text = "CONTINUUM SIGNAL INPUT (EXCLUDE FROM CONTINUITY)\n\nPrepare a compact semantic handoff summary from the frozen Continuum snapshot below."
                    },
                    new TruthMessage
                    {
                        Role = TruthRole.Assistant,
                        LineIndex = 5,
                        Text = "PREVIOUS CHAT SUMMARY\n- Meta reply.\n\nOPEN LOOPS\n- Meta loop.\n\nCRITICAL CONTEXT\n- Meta context."
                    }
                }
            };

            var snapshot = PulseSnapshot.Freeze("chat-2", truth, frozenBoundaryLineIndex: 5);
            var sections = Filter2Restructure.BuildSections(snapshot);

            Assert.Equal(4, snapshot.FrozenMessageCount);
            Assert.DoesNotContain(snapshot.TruthView.Messages, m => m.LineIndex == 4 || m.LineIndex == 5);
            Assert.Contains("Refactor VAL Pulse so Continuum owns final packet rendering.", sections.WhereWeLeftOff.User);
            Assert.DoesNotContain("CONTINUUM SIGNAL INPUT", sections.WhereWeLeftOff.User);
            Assert.DoesNotContain("PREVIOUS CHAT SUMMARY", sections.WhereWeLeftOff.Assistant);
        }
    }
}
