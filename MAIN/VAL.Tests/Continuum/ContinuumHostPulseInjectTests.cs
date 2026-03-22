using VAL.Continuum;
using Xunit;

namespace VAL.Tests.Continuum
{
    public sealed class ContinuumHostPulseInjectTests
    {
        [Theory]
        [InlineData("refresh.inject.success:Pulse:new_chat", "Pulse", "new_chat")]
        [InlineData("refresh.inject.success:Pulse:new_chat_root", "Pulse", "new_chat_root")]
        [InlineData("refresh.inject.success:Signal:current_chat", "Signal", "current_chat")]
        public void TryParseRefreshInjectSuccessParsesModeAndLabel(string evt, string expectedMode, string expectedLabel)
        {
            var ok = ContinuumHost.TryParseRefreshInjectSuccess(evt, out var mode, out var label);

            Assert.True(ok);
            Assert.Equal(expectedMode, mode);
            Assert.Equal(expectedLabel, label);
        }

        [Theory]
        [InlineData("refresh.inject.success")]
        [InlineData("")]
        [InlineData("refresh.inject.success:Pulse")]
        public void TryParseRefreshInjectSuccessRejectsIncompleteEvents(string evt)
        {
            var ok = ContinuumHost.TryParseRefreshInjectSuccess(evt, out _, out _);

            Assert.False(ok);
        }

        [Theory]
        [InlineData("new_chat", true)]
        [InlineData("new_chat_root", true)]
        [InlineData("current_chat", false)]
        [InlineData("current_chat_fallback", false)]
        [InlineData("", false)]
        public void IsPulseCompletionTargetAllowsOnlyNewChatTargets(string label, bool expected)
        {
            var actual = ContinuumHost.IsPulseCompletionTarget(label);

            Assert.Equal(expected, actual);
        }
    }
}
