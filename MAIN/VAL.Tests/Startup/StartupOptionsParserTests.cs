using VAL.Host.Startup;
using Xunit;

namespace VAL.Tests.Startup
{
    public sealed class StartupOptionsParserTests
    {
        private static readonly string[] SafeArgs = { "--safe" };
        private static readonly string[] NoModulesArgs = { "--nomodules" };
        private static readonly string[] UnknownArgs = { "--unknown" };

        [Fact]
        public void ParseSafeFlagSetsSafeModeExplicit()
        {
            var options = StartupOptionsParser.Parse(SafeArgs);

            Assert.True(options.SafeMode);
            Assert.True(options.SafeModeExplicit);
        }

        [Fact]
        public void ParseNoModulesFlagSetsSafeModeExplicit()
        {
            var options = StartupOptionsParser.Parse(NoModulesArgs);

            Assert.True(options.SafeMode);
            Assert.True(options.SafeModeExplicit);
        }

        [Fact]
        public void ParseUnknownArgsIgnoresUnknown()
        {
            var options = StartupOptionsParser.Parse(UnknownArgs);

            Assert.False(options.SafeMode);
            Assert.False(options.SafeModeExplicit);
        }
    }
}
