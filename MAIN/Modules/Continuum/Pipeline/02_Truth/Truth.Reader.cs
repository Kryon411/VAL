using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VAL.Continuum.Pipeline.Truth
{
    internal readonly record struct TruthEntry(int LineNumber, char Role, string Payload);

    internal static class TruthReader
    {
        internal static IEnumerable<TruthEntry> Read(string truthPath, bool repairTailFirst = true)
        {
            if (string.IsNullOrWhiteSpace(truthPath))
                yield break;

            if (!File.Exists(truthPath))
                yield break;

            try
            {
                if (repairTailFirst)
                    TruthFile.TryRepairTruncatedTail(truthPath, out _);
            }
            catch
            {
                // best-effort repair; never block reading
            }

            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            int lineNumber = 0;
            try
            {
                using var fs = new FileStream(truthPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs, encoding, detectEncodingFromByteOrderMarks: false);
                while (true)
                {
                    var line = reader.ReadLine();
                    if (line == null)
                        yield break;

                    lineNumber++;

                    if (!TruthLine.TryParse(line, out var role, out var payload))
                        continue;

                    yield return new TruthEntry(lineNumber, role, payload);
                }
            }
            catch
            {
                yield break;
            }
        }
    }
}
