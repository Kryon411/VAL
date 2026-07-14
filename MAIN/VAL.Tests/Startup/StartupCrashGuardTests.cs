using System;
using System.IO;
using System.Text.Json;

using VAL.App.Host.Startup;
using VAL.Host;

using Xunit;

namespace VAL.Tests.Startup
{
    public sealed class StartupCrashGuardTests
    {
        [Fact]
        public void EvaluateWhenStateIsMissingDoesNotEnterSafeMode()
        {
            var dataRoot = CreateTempRoot();
            var guard = new StartupCrashGuard(new FakeLog(), dataRoot);

            var safeMode = guard.EvaluateAndMarkStarting();

            Assert.False(safeMode);
        }

        [Fact]
        public void EvaluateWhenCrashCountAtThresholdEntersSafeMode()
        {
            var dataRoot = CreateTempRoot();
            WriteState(dataRoot, 2);
            var guard = new StartupCrashGuard(new FakeLog(), dataRoot);

            var safeMode = guard.EvaluateAndMarkStarting();

            Assert.True(safeMode);
        }

        [Fact]
        public void MarkSuccessResetsCrashCount()
        {
            var dataRoot = CreateTempRoot();
            WriteState(dataRoot, 3);
            var guard = new StartupCrashGuard(new FakeLog(), dataRoot);

            guard.MarkSuccess();

            var statePath = Path.Combine(dataRoot, "State", "startup.json");
            using var stream = File.OpenRead(statePath);
            using var doc = JsonDocument.Parse(stream);
            var crashes = doc.RootElement.GetProperty("consecutiveStartupCrashes").GetInt32();

            Assert.Equal(0, crashes);
        }

        [Fact]
        public void EvaluateWhenStateCorruptEntersSafeMode()
        {
            var dataRoot = CreateTempRoot();
            var statePath = Path.Combine(dataRoot, "State", "startup.json");
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            File.WriteAllText(statePath, "not-json");
            var guard = new StartupCrashGuard(new FakeLog(), dataRoot);

            var safeMode = guard.EvaluateAndMarkStarting();

            Assert.True(safeMode);
        }

        private static string CreateTempRoot()
        {
            var path = Path.Combine(Path.GetTempPath(), "val-startup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void WriteState(string dataRoot, int crashCount)
        {
            var statePath = Path.Combine(dataRoot, "State", "startup.json");
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            var json = JsonSerializer.Serialize(new
            {
                consecutiveStartupCrashes = crashCount,
                lastStartUtc = "2024-01-01T00:00:00Z",
                lastSuccessUtc = "2024-01-01T00:00:00Z"
            });
            File.WriteAllText(statePath, json);
        }

        private sealed class FakeLog : ILog
        {
            public void Info(string category, string message) { }
            public void Warn(string category, string message) { }
            public void LogError(string category, string message) { }
            public void Verbose(string category, string message) { }
        }
    }
}
