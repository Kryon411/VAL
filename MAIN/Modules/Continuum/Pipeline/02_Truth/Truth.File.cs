using System;
using System.IO;

namespace VAL.Continuum.Pipeline.Truth
{
    internal static class TruthFile
    {
        internal static bool TryRepairTruncatedTail(string truthPath, out long bytesRemoved)
        {
            bytesRemoved = 0;

            if (string.IsNullOrWhiteSpace(truthPath))
                return false;

            try
            {
                if (!File.Exists(truthPath))
                    return false;

                using var fs = new FileStream(truthPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                if (fs.Length == 0)
                    return false;

                var originalLength = fs.Length;
                fs.Position = originalLength - 1;
                var lastByte = fs.ReadByte();
                if (lastByte == -1 || lastByte == (byte)'\n')
                    return false;

                var buffer = new byte[64 * 1024];
                long scanPos = originalLength;
                long lastNewlinePos = -1;

                while (scanPos > 0 && lastNewlinePos < 0)
                {
                    var readSize = (int)Math.Min(buffer.Length, scanPos);
                    scanPos -= readSize;
                    fs.Position = scanPos;
                    var read = fs.Read(buffer, 0, readSize);
                    for (var i = read - 1; i >= 0; i--)
                    {
                        if (buffer[i] == (byte)'\n')
                        {
                            lastNewlinePos = scanPos + i;
                            break;
                        }
                    }
                }

                var newLength = lastNewlinePos >= 0 ? lastNewlinePos + 1 : 0;
                bytesRemoved = originalLength - newLength;
                if (bytesRemoved <= 0)
                    return false;

                fs.SetLength(newLength);
                fs.Flush(flushToDisk: true);
                return true;
            }
            catch
            {
                bytesRemoved = 0;
                return false;
            }
        }
    }
}
