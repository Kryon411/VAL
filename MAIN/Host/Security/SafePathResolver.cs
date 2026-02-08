using System;
using System.IO;
using VAL.Continuum.Pipeline.Truth;

namespace VAL.Host.Security
{
    internal static class SafePathResolver
    {
        public static bool TryResolveChatTruthPath(string productRoot, string chatId, out string truthPath, out string chatDir)
        {
            truthPath = string.Empty;
            chatDir = string.Empty;

            if (string.IsNullOrWhiteSpace(productRoot) || string.IsNullOrWhiteSpace(chatId))
                return false;

            if (!Guid.TryParse(chatId, out _))
                return false;

            if (chatId.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
                return false;

            try
            {
                var memoryRoot = Path.Combine(productRoot, "Memory", "Chats");
                var fullMemoryRoot = Path.GetFullPath(memoryRoot);
                var fullChatDir = Path.GetFullPath(Path.Combine(memoryRoot, chatId));

                if (!fullMemoryRoot.EndsWith(Path.DirectorySeparatorChar))
                    fullMemoryRoot += Path.DirectorySeparatorChar;

                if (!fullChatDir.StartsWith(fullMemoryRoot, StringComparison.OrdinalIgnoreCase))
                    return false;

                chatDir = fullChatDir;
                truthPath = Path.Combine(fullChatDir, TruthStorage.TruthFileName);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
