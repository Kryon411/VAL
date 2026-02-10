using System;
using System.IO;
using System.Linq;
using System.Threading;
using VAL.Host.Logging;
using Xunit;

namespace VAL.Tests.Logging
{
    public sealed class RollingFileLogSinkTests
    {
        [Fact]
        public void WriteCreatesLogFile()
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
        public void WriteRotatesWhenSizeExceeded()
        {
            var dir = Path.Combine(Path.GetTempPath(), "val-log-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "VAL.log");
            var sink = new RollingFileLogSink(path, maxBytes: 4096, maxFiles: 3);
            var rotated = Path.Combine(dir, "VAL.1.log");

            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(2);
                var payload = new string('a', 2048);
                while (!File.Exists(rotated) && DateTime.UtcNow < deadline)
                {
                    sink.Write(CreateEvent(payload));
                    Thread.Sleep(10);
                }

                Assert.True(File.Exists(path));
                Assert.True(File.Exists(rotated), DumpDirectoryState(dir));
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

        private static string DumpDirectoryState(string dir)
        {
            try
            {
                if (!Directory.Exists(dir))
                    return $"Missing directory: {dir}";

                var files = Directory.GetFiles(dir);
                if (files.Length == 0)
                    return $"No files in {dir}";

                return string.Join(Environment.NewLine,
                    files.Select(path => $"{Path.GetFileName(path)} ({new FileInfo(path).Length} bytes)"));
            }
            catch (Exception ex)
            {
                return $"Failed to dump directory state: {ex.GetType().Name}";
            }
        }
    }
}
