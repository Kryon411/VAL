using System;
using VAL.Continuum.Pipeline.Truth;
using Xunit;

namespace VAL.Tests.Truth
{
    public sealed class TruthLineTests
    {
        [Theory]
        [InlineData("A|hello", 'A', "hello")]
        [InlineData("U|", 'U', "")]
        [InlineData("A|line1\\nline2", 'A', "line1\\nline2")]
        [InlineData("A|hi|extra", 'A', "hi|extra")]
        public void TryParse_ValidLines_ParseRoleAndPayload(string line, char expectedRole, string expectedPayload)
        {
            var result = TruthLine.TryParse(line, out var role, out var payload);

            Assert.True(result);
            Assert.Equal(expectedRole, role);
            Assert.Equal(expectedPayload, payload);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("X|hi")]
        [InlineData("Ahi")]
        public void TryParse_InvalidLines_ReturnsFalse(string line)
        {
            var ex = Record.Exception(() => TruthLine.TryParse(line, out var role, out var payload));

            Assert.Null(ex);
            Assert.False(TruthLine.TryParse(line, out var role, out var payload));
            Assert.Equal('\0', role);
            Assert.Equal(string.Empty, payload);
        }
    }
}
