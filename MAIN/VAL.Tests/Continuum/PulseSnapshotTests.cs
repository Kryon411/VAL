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
                        Text = "Please write a compact, high-signal THREAD STATE SUMMARY for the current chat thread.\n\nSummarize the most important state of the discussion immediately before this request.\nFocus on decisions, completed work, current direction, unresolved questions, concrete constraints, and important context that should be preserved.\nExclude this request itself and any meta discussion about summarizing or moving between chats.\nOutput exactly:\n\nTHREAD STATE SUMMARY\n- "
                    },
                    new TruthMessage
                    {
                        Role = TruthRole.Assistant,
                        LineIndex = 5,
                        Text = "THREAD STATE SUMMARY\n- Continuum owns the final packet shape now.\n- Signal only needs to supply PCS."
                    }
                }
            };

            var snapshot = PulseSnapshot.Freeze("chat-2", truth, frozenBoundaryLineIndex: 5);
            var sections = Filter2Restructure.BuildSections(snapshot);

            Assert.Equal(4, snapshot.FrozenMessageCount);
            Assert.DoesNotContain(snapshot.TruthView.Messages, m => m.LineIndex == 4 || m.LineIndex == 5);
            Assert.Contains("Refactor VAL Pulse so Continuum owns final packet rendering.", sections.WhereWeLeftOff.User);
            Assert.DoesNotContain("Please write a compact, high-signal THREAD STATE SUMMARY", sections.WhereWeLeftOff.User);
            Assert.DoesNotContain("THREAD STATE SUMMARY", sections.WhereWeLeftOff.Assistant);
        }

        [Fact]
        public void FreezeStillExcludesLegacyPreviousChatSummaryTurns()
        {
            var truth = new TruthView
            {
                ChatId = "chat-legacy",
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
                        Text = "Please write a compact, high-signal PREVIOUS CHAT SUMMARY for the current chat thread.\n\nSummarize the most important state of the discussion immediately before this request.\nOutput exactly:\n\nPREVIOUS CHAT SUMMARY\n- "
                    },
                    new TruthMessage
                    {
                        Role = TruthRole.Assistant,
                        LineIndex = 3,
                        Text = "PREVIOUS CHAT SUMMARY\n- Legacy summaries should still be filtered."
                    }
                }
            };

            var snapshot = PulseSnapshot.Freeze("chat-legacy", truth, frozenBoundaryLineIndex: 3);

            Assert.Equal(2, snapshot.FrozenMessageCount);
            Assert.DoesNotContain(snapshot.TruthView.Messages, m => m.LineIndex == 2 || m.LineIndex == 3);
        }
    }
}
