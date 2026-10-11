using VAL.Continuum.Pipeline.Filter1;
using VAL.Continuum.Pipeline.Filter2;
using VAL.Continuum.Pipeline.Truth;

using Xunit;

namespace VAL.Tests.Continuum;

public sealed class Filter1SafetyTests
{
    [Fact]
    public void RepliesWithTheSameLongPrefixRemainDistinct()
    {
        var prefix = new string('x', 4200);
        var result = Filter1BuildSeed.BuildSeed(new TruthView
        {
            Messages = new[]
            {
                new TruthMessage { Role = TruthRole.User, Text = "first", LineIndex = 0 },
                new TruthMessage { Role = TruthRole.Assistant, Text = prefix + " first conclusion", LineIndex = 1 },
                new TruthMessage { Role = TruthRole.User, Text = "second", LineIndex = 2 },
                new TruthMessage { Role = TruthRole.Assistant, Text = prefix + " second conclusion", LineIndex = 3 },
            }
        });

        Assert.Equal(2, result.Exchanges.Count);
        Assert.Contains("second conclusion", result.Exchanges[1].AssistantTextUncut);
        Assert.Contains("Source: Truth 1", result.SeedLogText);
        Assert.Contains("Source: Truth 3-4", Filter2Restructure.BuildRestructuredSeed(result.Exchanges));
    }

    [Fact]
    public void ParagraphsWithTheSameLongPrefixAreNotDiscarded()
    {
        var prefix = new string('x', 600);
        var result = Filter1Rules.FilterUserUncut(prefix + " first conclusion\n\n" + prefix + " second conclusion");
        Assert.Contains("first conclusion", result);
        Assert.Contains("second conclusion", result);
    }
}
