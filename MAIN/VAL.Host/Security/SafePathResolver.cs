using System;
using System.IO;

using VAL.Continuum.Pipeline.Truth;

namespace VAL.Host.Security
{
    public static class SafePathResolver
    {
        public static bool TryResolveChatTruthPath(string memoryChatsRoot, string chatId, out string truthPath, out string chatDir)
        {
            truthPath = string.Empty;
            chatDir = string.Empty;

            if (string.IsNullOrWhiteSpace(memoryChatsRoot) || string.IsNullOrWhiteSpace(chatId))
                return false;

            if (!Guid.TryParse(chatId, out _))
                return false;

            if (chatId.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
                return false;

            try
            {
                var fullMemoryRoot = Path.GetFullPath(memoryChatsRoot);
                var fullChatDir = Path.GetFullPath(Path.Combine(fullMemoryRoot, chatId));

                if (!fullMemoryRoot.EndsWith(Path.DirectorySeparatorChar))
                    fullMemoryRoot += Path.DirectorySeparatorChar;

                if (!fullChatDir.StartsWith(fullMemoryRoot, StringComparison.OrdinalIgnoreCase))
                    return false;

                chatDir = fullChatDir;
                truthPath = Path.Combine(fullChatDir, TruthStore.DefaultTruthFileName);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
