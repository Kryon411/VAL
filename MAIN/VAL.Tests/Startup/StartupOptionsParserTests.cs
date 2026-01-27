using VAL.Host.Startup;
using Xunit;

namespace VAL.Tests.Startup
{
    public sealed class StartupOptionsParserTests
    {
        [Fact]
        public void Parse_SafeFlag_SetsSafeModeExplicit()
        {
            var options = StartupOptionsParser.Parse(new[] { "--safe" });

            Assert.True(options.SafeMode);
            Assert.True(options.SafeModeExplicit);
        }

        [Fact]
        public void Parse_NoModulesFlag_SetsSafeModeExplicit()
        {
            var options = StartupOptionsParser.Parse(new[] { "--nomodules" });

            Assert.True(options.SafeMode);
            Assert.True(options.SafeModeExplicit);
        }

        [Fact]
        public void Parse_UnknownArgs_IgnoresUnknown()
        {
            var options = StartupOptionsParser.Parse(new[] { "--unknown" });

            Assert.False(options.SafeMode);
            Assert.False(options.SafeModeExplicit);
        }
    }
}
