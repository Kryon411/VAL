using System;
using System.IO;
using VAL.Continuum.Pipeline.Truth;
using Xunit;

namespace VAL.Tests.Truth
{
    public sealed class TruthFileRepairTests
    {
        [Fact]
        public void TryRepairTruncatedTailNoRepairNeededReturnsFalse()
        {
            var (path, cleanup) = CreateTempFile();
            try
            {
                File.WriteAllText(path, "A|ok\r\n");

                var repaired = TruthFile.TryRepairTruncatedTail(path, out var bytesRemoved);
                var contents = File.ReadAllText(path);

                Assert.False(repaired);
                Assert.Equal(0, bytesRemoved);
                Assert.Equal("A|ok\r\n", contents);
            }
            finally
            {
                cleanup();
            }
        }

        [Fact]
        public void TryRepairTruncatedTailRemovesPartialLine()
        {
            var (path, cleanup) = CreateTempFile();
            try
            {
                File.WriteAllText(path, "A|ok\r\nU|partial");

                var repaired = TruthFile.TryRepairTruncatedTail(path, out var bytesRemoved);
                var contents = File.ReadAllText(path);

                Assert.True(repaired);
                Assert.True(bytesRemoved > 0);
                Assert.Equal("A|ok\r\n", contents);
            }
            finally
            {
                cleanup();
            }
        }

        [Fact]
        public void TryRepairTruncatedTailNoNewlineTruncatesAll()
        {
            var (path, cleanup) = CreateTempFile();
            try
            {
                File.WriteAllText(path, "A|partial");

                var repaired = TruthFile.TryRepairTruncatedTail(path, out var bytesRemoved);
                var info = new FileInfo(path);

                Assert.True(repaired);
                Assert.True(bytesRemoved > 0);
                Assert.Equal(0, info.Length);
            }
            finally
            {
                cleanup();
            }
        }

        [Fact]
        public void TryRepairTruncatedTailIsIdempotent()
        {
            var (path, cleanup) = CreateTempFile();
            try
            {
                File.WriteAllText(path, "A|ok\r\nU|partial");

                var first = TruthFile.TryRepairTruncatedTail(path, out _);
                var second = TruthFile.TryRepairTruncatedTail(path, out var bytesRemoved);

                Assert.True(first);
                Assert.False(second);
                Assert.Equal(0, bytesRemoved);
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
