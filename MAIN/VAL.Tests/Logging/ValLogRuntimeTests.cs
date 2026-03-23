using System;
using System.Collections.Generic;
using System.IO;
using VAL.Host.Logging;
using Xunit;

namespace VAL.Tests.Logging
{
    public sealed class ValLogRuntimeTests
    {
        [Fact]
        public void GetRecentReturnsEventsInWriteOrder()
        {
            var runtime = new ValLogRuntime(CreateTempLogPath());

            runtime.Info("Test", "first");
            runtime.Warn("Test", "second");

            var recent = runtime.GetRecent();

            Assert.Equal(2, recent.Count);
            Assert.Equal("first", recent[0].Message);
            Assert.Equal("second", recent[1].Message);
        }

        [Fact]
        public void AddSinkSkipsDuplicateInstances()
        {
            var runtime = new ValLogRuntime(CreateTempLogPath());
            var sink = new RecordingSink();

            runtime.AddSink(sink);
            runtime.AddSink(sink);
            runtime.Info("Test", "message");

            Assert.Single(sink.Events);
        }

        [Fact]
        public void VerboseHonorsConfiguration()
        {
            var runtime = new ValLogRuntime(CreateTempLogPath());
            var sink = new RecordingSink();
            runtime.AddSink(sink);

            runtime.Verbose("Test", "hidden");
            runtime.Configure(null, enableVerboseLogging: true);
            runtime.Verbose("Test", "visible");

            Assert.Single(sink.Events);
            Assert.Equal(LogLevel.Verbose, sink.Events[0].Level);
            Assert.Equal("visible", sink.Events[0].Message);
        }

        private static string CreateTempLogPath()
        {
            var dir = Path.Combine(Path.GetTempPath(), "val-log-runtime-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "VAL.log");
        }

        private sealed class RecordingSink : ILogSink
        {
            public List<LogEvent> Events { get; } = new();

            public void Write(LogEvent logEvent)
            {
                Events.Add(logEvent);
            }
        }
    }
}
