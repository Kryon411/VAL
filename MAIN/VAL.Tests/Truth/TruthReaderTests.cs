using System;
using System.IO;
using System.Linq;
using VAL.Continuum.Pipeline.Truth;
using Xunit;

namespace VAL.Tests.Truth
{
    public sealed class TruthReaderTests
    {
        [Fact]
        public void ReadSkipsMalformedLinesAndMaintainsLineNumbers()
        {
            var (path, cleanup) = CreateTempFile();
            try
            {
                File.WriteAllText(path, string.Join(string.Empty, new[]
                {
                    "A|ok\r\n",
                    "BAD LINE\r\n",
                    "U|two\r\n",
                    "X|nope\r\n",
                    "\r\n",
                    "   \r\n"
                }));

                var ex = Record.Exception(() => TruthReader.Read(path, repairTailFirst: true).ToList());
                Assert.Null(ex);

                var entries = TruthReader.Read(path, repairTailFirst: true).ToList();

                Assert.Equal(2, entries.Count);
                Assert.Equal(1, entries[0].LineNumber);
                Assert.Equal('A', entries[0].Role);
                Assert.Equal("ok", entries[0].Payload);
                Assert.Equal(3, entries[1].LineNumber);
                Assert.Equal('U', entries[1].Role);
                Assert.Equal("two", entries[1].Payload);
            }
            finally
            {
                cleanup();
            }
        }

        private static (string path, Action cleanup) CreateTempFile()
        {
            var dir = Path.Combine(Path.GetTempPath(), "VAL.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "Truth.log");

            return (path, () =>
            {
                try
                {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, recursive: true);
                }
                catch
                {
                    // best-effort cleanup
                }
            });
        }
    }
}
