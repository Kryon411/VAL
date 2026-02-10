using System;
using System.IO;
using System.Text.Json;
using VAL.Host.Startup;
using Xunit;

namespace VAL.Tests.Startup
{
    public sealed class StartupCrashGuardTests
    {
        [Fact]
        public void EvaluateWhenCrashCountAtThresholdEntersSafeMode()
        {
            var productRoot = CreateTempRoot();
            WriteState(productRoot, 2);
            var guard = new StartupCrashGuard(productRoot);

            var safeMode = guard.EvaluateAndMarkStarting();

            Assert.True(safeMode);
        }

        [Fact]
        public void MarkSuccessResetsCrashCount()
        {
            var productRoot = CreateTempRoot();
            WriteState(productRoot, 3);
            var guard = new StartupCrashGuard(productRoot);

            guard.MarkSuccess();

            var statePath = Path.Combine(productRoot, "State", "startup.json");
            using var stream = File.OpenRead(statePath);
            using var doc = JsonDocument.Parse(stream);
            var crashes = doc.RootElement.GetProperty("consecutiveStartupCrashes").GetInt32();

            Assert.Equal(0, crashes);
        }

        [Fact]
        public void EvaluateWhenStateCorruptEntersSafeMode()
        {
            var productRoot = CreateTempRoot();
            var statePath = Path.Combine(productRoot, "State", "startup.json");
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            File.WriteAllText(statePath, "not-json");
            var guard = new StartupCrashGuard(productRoot);

            var safeMode = guard.EvaluateAndMarkStarting();

            Assert.True(safeMode);
        }

        private static string CreateTempRoot()
        {
            var path = Path.Combine(Path.GetTempPath(), "val-startup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void WriteState(string productRoot, int crashCount)
        {
            var statePath = Path.Combine(productRoot, "State", "startup.json");
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            var json = JsonSerializer.Serialize(new
            {
                consecutiveStartupCrashes = crashCount,
                lastStartUtc = "2024-01-01T00:00:00Z",
                lastSuccessUtc = "2024-01-01T00:00:00Z"
            });
            File.WriteAllText(statePath, json);
        }
    }
}
