using VAL.Continuum;

using Xunit;

namespace VAL.Tests.Continuum
{
    public sealed class ContinuumTruthCaptureParserTests
    {
        [Theory]
        [InlineData("assistant", 'A')]
        [InlineData("a", 'A')]
        [InlineData("user", 'U')]
        [InlineData(null, 'U')]
        public void ParseRoleNormalizesKnownRoles(string? value, char expected)
        {
            Assert.Equal(expected, ContinuumTruthCaptureParser.ParseRole(value));
        }

        [Fact]
        public void TryParseLegacyLineExtractsRoleAndPayload()
        {
            var parsed = ContinuumTruthCaptureParser.TryParseLegacyLine(
                "[2026-07-14T12:00:00Z][A] response text",
                out var role,
                out var text);

            Assert.True(parsed);
            Assert.Equal('A', role);
            Assert.Equal("response text", text);
        }

        [Fact]
        public void TryParseLegacyLineRejectsBlankInput()
        {
            Assert.False(ContinuumTruthCaptureParser.TryParseLegacyLine(" ", out _, out _));
        }

        [Theory]
        [InlineData("ESSENCE-M SNAPSHOT (AUTHORITATIVE)", true)]
        [InlineData("ordinary conversation", false)]
        [InlineData(null, false)]
        public void SeedClassifierRecognizesAuthoritativeMarkers(string? value, bool expected)
        {
            Assert.Equal(expected, ContinuumSeedClassifier.IsContinuumSeed(value));
        }
    }
}
