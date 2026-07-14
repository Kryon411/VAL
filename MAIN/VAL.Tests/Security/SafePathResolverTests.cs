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
            var memoryChatsRoot = Path.Combine(Path.GetTempPath(), "VAL", Guid.NewGuid().ToString("N"), "Memory", "Chats");
            var chatId = Guid.NewGuid().ToString();

            var result = SafePathResolver.TryResolveChatTruthPath(memoryChatsRoot, chatId, out var truthPath, out var chatDir);

            Assert.True(result);
            var expectedChatDir = Path.GetFullPath(Path.Combine(memoryChatsRoot, chatId));
            Assert.Equal(expectedChatDir, chatDir);
            Assert.Equal(Path.Combine(expectedChatDir, TruthStore.DefaultTruthFileName), truthPath);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-a-guid")]
        public void TryResolveChatTruthPathRejectsInvalidChatId(string chatId)
        {
            var memoryChatsRoot = Path.Combine(Path.GetTempPath(), "VAL", Guid.NewGuid().ToString("N"), "Memory", "Chats");

            var result = SafePathResolver.TryResolveChatTruthPath(memoryChatsRoot, chatId, out _, out _);

            Assert.False(result);
        }

        [Theory]
        [InlineData("..\\..\\Windows")]
        [InlineData("../etc/passwd")]
        public void TryResolveChatTruthPathRejectsTraversal(string chatId)
        {
            var memoryChatsRoot = Path.Combine(Path.GetTempPath(), "VAL", Guid.NewGuid().ToString("N"), "Memory", "Chats");

            var result = SafePathResolver.TryResolveChatTruthPath(memoryChatsRoot, chatId, out _, out _);

            Assert.False(result);
        }

        [Fact]
        public void TryResolveChatTruthPathEnforcesContainment()
        {
            var memoryChatsRoot = Path.Combine(Path.GetTempPath(), "VAL", "Memory", "..", "Memory", "Chats");
            var chatId = Guid.NewGuid().ToString();

            var result = SafePathResolver.TryResolveChatTruthPath(memoryChatsRoot, chatId, out var truthPath, out var chatDir);

            Assert.True(result);
            var expectedRoot = Path.GetFullPath(memoryChatsRoot) + Path.DirectorySeparatorChar;
            Assert.StartsWith(expectedRoot, chatDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(expectedRoot, Path.GetDirectoryName(truthPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }
}
