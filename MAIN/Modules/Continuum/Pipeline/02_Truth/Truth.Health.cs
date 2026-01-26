using System;
using System.Globalization;
using System.IO;

namespace VAL.Continuum.Pipeline.Truth
{
    internal sealed record TruthHealthReport(
        string ChatId,
        string TruthPath,
        long Bytes,
        int PhysicalLineCount,
        int ParsedEntryCount,
        int LastParsedPhysicalLineNumber,
        DateTime? LastRepairUtc,
        long? LastRepairBytesRemoved);

    internal static class TruthHealth
    {
        internal static TruthHealthReport Build(string chatId, string truthPath, string repairLogPath)
        {
            return Build(chatId, truthPath, repairLogPath, repairTailFirst: true);
        }

        internal static TruthHealthReport Build(string chatId, string truthPath, string repairLogPath, bool repairTailFirst)
        {
            long bytes = 0;
            int physicalLineCount = 0;
            int parsedEntryCount = 0;
            int lastParsedLineNumber = 0;
            DateTime? lastRepairUtc = null;
            long? lastRepairBytesRemoved = null;

            try
            {
                if (!string.IsNullOrWhiteSpace(truthPath) && File.Exists(truthPath))
                {
                    bytes = new FileInfo(truthPath).Length;
                    physicalLineCount = CountPhysicalLines(truthPath, bytes);

                    foreach (var entry in TruthReader.Read(truthPath, repairTailFirst))
                    {
                        parsedEntryCount++;
                        if (entry.LineNumber > lastParsedLineNumber)
                            lastParsedLineNumber = entry.LineNumber;
                    }
                }
            }
            catch
            {
                // best-effort only
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(repairLogPath) && File.Exists(repairLogPath))
                {
                    var lastLine = ReadLastNonEmptyLine(repairLogPath);
                    if (!string.IsNullOrWhiteSpace(lastLine))
                    {
                        if (TryParseRepairLogLine(lastLine, out var parsedUtc, out var bytesRemoved))
                        {
                            lastRepairUtc = parsedUtc;
                            lastRepairBytesRemoved = bytesRemoved;
                        }
                    }
                }
            }
            catch
            {
                // best-effort only
            }

            return new TruthHealthReport(
                chatId ?? string.Empty,
                truthPath ?? string.Empty,
                bytes,
                physicalLineCount,
                parsedEntryCount,
                lastParsedLineNumber,
                lastRepairUtc,
                lastRepairBytesRemoved);
        }

        private static int CountPhysicalLines(string truthPath, long bytes)
        {
            if (bytes <= 0)
                return 0;

            var buffer = new byte[64 * 1024];
            int lineCount = 0;
            int lastByte = -1;

            try
            {
                using var fs = new FileStream(truthPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                while (true)
                {
                    var read = fs.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                        break;

                    lastByte = buffer[read - 1];
                    for (var i = 0; i < read; i++)
                    {
                        if (buffer[i] == (byte)'\n')
                            lineCount++;
                    }
                }
            }
            catch
            {
                return 0;
            }

            if (lastByte != (byte)'\n')
                lineCount++;

            return lineCount;
        }

        private static string ReadLastNonEmptyLine(string path)
        {
            string last = string.Empty;
            foreach (var line in File.ReadLines(path))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    last = line.Trim();
            }

            return last;
        }

        private static bool TryParseRepairLogLine(string line, out DateTime utc, out long bytesRemoved)
        {
            utc = default;
            bytesRemoved = 0;

            if (string.IsNullOrWhiteSpace(line))
                return false;

            var firstSpace = line.IndexOf(' ');
            if (firstSpace <= 0)
                return false;

            var stamp = line.Substring(0, firstSpace);
            if (!DateTime.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out utc))
                return false;

            var removedIndex = line.IndexOf("removed", StringComparison.OrdinalIgnoreCase);
            if (removedIndex < 0)
                return true;

            var numberStart = removedIndex + "removed".Length;
            while (numberStart < line.Length && line[numberStart] == ' ')
                numberStart++;

            var numberEnd = numberStart;
            while (numberEnd < line.Length && char.IsDigit(line[numberEnd]))
                numberEnd++;

            if (numberEnd <= numberStart)
                return true;

            var numberSlice = line.Substring(numberStart, numberEnd - numberStart);
            if (long.TryParse(numberSlice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                bytesRemoved = parsed;
                return true;
            }

            return true;
        }
    }
}
