using System;
using System.IO;
using VAL.Continuum.Pipeline.Truth;
using Xunit;

namespace VAL.Tests.Truth
{
    public sealed class TruthHealthTests
    {
        [Fact]
        public void BuildReportsCountsAndLastParsedLine()
        {
            var root = Path.Combine(Path.GetTempPath(), "val-truth-health", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                var truthPath = Path.Combine(root, "Truth.log");
                var lines = new[]
                {
                    "U|hello",
                    "not-a-truth-line",
                    "A|reply"
                };

                File.WriteAllText(truthPath, string.Join(Environment.NewLine, lines));

                var report = TruthHealth.Build("chat-1", truthPath, Path.Combine(root, "Truth.repair.log"));

                Assert.Equal(3, report.PhysicalLineCount);
                Assert.Equal(2, report.ParsedEntryCount);
                Assert.Equal(3, report.LastParsedPhysicalLineNumber);
                Assert.True(report.Bytes > 0);
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }
    }
}
