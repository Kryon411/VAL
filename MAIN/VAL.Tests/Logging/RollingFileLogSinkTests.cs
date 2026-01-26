using System;
using System.IO;
using VAL.Host.Logging;
using Xunit;

namespace VAL.Tests.Logging
{
    public sealed class RollingFileLogSinkTests
    {
        [Fact]
        public void Write_CreatesLogFile()
        {
            var dir = Path.Combine(Path.GetTempPath(), "val-log-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "VAL.log");
            var sink = new RollingFileLogSink(path, maxBytes: 1024, maxFiles: 2);

            try
            {
                sink.Write(CreateEvent("hello"));

                Assert.True(File.Exists(path));
            }
            finally
            {
                TryDelete(dir);
            }
        }

        [Fact]
        public void Write_RotatesWhenSizeExceeded()
        {
            var dir = Path.Combine(Path.GetTempPath(), "val-log-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "VAL.log");
            var sink = new RollingFileLogSink(path, maxBytes: 80, maxFiles: 3);

            try
            {
                for (var i = 0; i < 5 && !File.Exists(Path.Combine(dir, "VAL.1.log")); i++)
                {
                    sink.Write(CreateEvent(new string('a', 120)));
                }

                Assert.True(File.Exists(path));
                Assert.True(File.Exists(Path.Combine(dir, "VAL.1.log")));
            }
            finally
            {
                TryDelete(dir);
            }
        }

        private static LogEvent CreateEvent(string message)
        {
            var line = $"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff} [INFO] [Test] {message}{Environment.NewLine}";
            return new LogEvent(DateTimeOffset.UtcNow, LogLevel.Info, "Test", message, line);
        }

        private static void TryDelete(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }
    }
}
