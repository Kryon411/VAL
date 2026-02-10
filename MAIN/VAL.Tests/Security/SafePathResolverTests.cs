using System;
using System.IO;
using VAL.Continuum.Pipeline.Truth;
using VAL.Host.Security;
using Xunit;

namespace VAL.Tests.Security
{
    public sealed class SafePathResolverTests
    {
        [Fact]
        public void TryResolveChatTruthPathAllowsValidGuid()
        {
            var productRoot = Path.Combine(Path.GetTempPath(), "VAL", Guid.NewGuid().ToString("N"));
            var chatId = Guid.NewGuid().ToString();

            var result = SafePathResolver.TryResolveChatTruthPath(productRoot, chatId, out var truthPath, out var chatDir);

            Assert.True(result);
            var expectedChatDir = Path.GetFullPath(Path.Combine(productRoot, "Memory", "Chats", chatId));
            Assert.Equal(expectedChatDir, chatDir);
            Assert.Equal(Path.Combine(expectedChatDir, TruthStorage.TruthFileName), truthPath);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-a-guid")]
        public void TryResolveChatTruthPathRejectsInvalidChatId(string chatId)
        {
            var productRoot = Path.Combine(Path.GetTempPath(), "VAL", Guid.NewGuid().ToString("N"));

            var result = SafePathResolver.TryResolveChatTruthPath(productRoot, chatId, out _, out _);

            Assert.False(result);
        }

        [Theory]
        [InlineData("..\\..\\Windows")]
        [InlineData("../etc/passwd")]
        public void TryResolveChatTruthPathRejectsTraversal(string chatId)
        {
            var productRoot = Path.Combine(Path.GetTempPath(), "VAL", Guid.NewGuid().ToString("N"));

            var result = SafePathResolver.TryResolveChatTruthPath(productRoot, chatId, out _, out _);

            Assert.False(result);
        }

        [Fact]
        public void TryResolveChatTruthPathEnforcesContainment()
        {
            var productRoot = Path.Combine(Path.GetTempPath(), "VAL", "Product", "..", "Product");
            var chatId = Guid.NewGuid().ToString();

            var result = SafePathResolver.TryResolveChatTruthPath(productRoot, chatId, out var truthPath, out var chatDir);

            Assert.True(result);
            var expectedRoot = Path.GetFullPath(Path.Combine(productRoot, "Memory", "Chats")) + Path.DirectorySeparatorChar;
            Assert.StartsWith(expectedRoot, chatDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(expectedRoot, Path.GetDirectoryName(truthPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }
}
