using System;
using System.IO;
using System.Text.Json;

using VAL.App.State;

using Xunit;

namespace VAL.Tests.State
{
    public sealed class JsonStateFileTests
    {
        [Fact]
        public void ReadWhenFileMissingReturnsMissing()
        {
            var path = CreateTempPath();

            var result = JsonStateFile.Read<TestState>(path);

            Assert.Equal(JsonStateFileReadStatus.Missing, result.Status);
            Assert.False(result.IsSuccess);
            Assert.Null(result.Value);
        }

        [Fact]
        public void ReadWhenFileEmptyReturnsEmpty()
        {
            var path = CreateTempPath();
            File.WriteAllText(path, string.Empty);

            var result = JsonStateFile.Read<TestState>(path);

            Assert.Equal(JsonStateFileReadStatus.Empty, result.Status);
            Assert.False(result.IsSuccess);
            Assert.Null(result.Value);
        }

        [Fact]
        public void ReadWhenFileInvalidReturnsInvalid()
        {
            var path = CreateTempPath();
            File.WriteAllText(path, "{ invalid");

            var result = JsonStateFile.Read<TestState>(path);

            Assert.Equal(JsonStateFileReadStatus.Invalid, result.Status);
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public void WriteThenReadRoundTripsValue()
        {
            var path = CreateTempPath();
            var expected = new TestState { Name = "VAL", Version = 7 };

            JsonStateFile.Write(path, expected, new JsonSerializerOptions { WriteIndented = true });
            var result = JsonStateFile.Read<TestState>(path);

            Assert.Equal(JsonStateFileReadStatus.Success, result.Status);
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(expected.Name, result.Value!.Name);
            Assert.Equal(expected.Version, result.Value.Version);
        }

        private static string CreateTempPath()
        {
            var directory = Path.Combine(Path.GetTempPath(), "val-state-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "state.json");
        }

        private sealed class TestState
        {
            public string Name { get; set; } = string.Empty;
            public int Version { get; set; }
        }
    }
}
