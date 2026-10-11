using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using VAL.Continuum.Pipeline.Truth;

using Xunit;

namespace VAL.Tests.Truth;

public sealed class TruthStoreSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "VAL.Tests", Guid.NewGuid().ToString("N"));
    private readonly TruthStore _store;

    public TruthStoreSafetyTests()
    {
        _store = new TruthStore(NullTruthTelemetryPublisher.Instance, _root);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("C:\\outside")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("chat:stream")]
    [InlineData("chat.")]
    [InlineData("CON")]
    public void UnsafeChatIdsCannotEscapeOrAliasAnArchive(string chatId)
    {
        Assert.ThrowsAny<ArgumentException>(() => _store.GetChatDir(chatId));
        Assert.False(_store.AppendTruthLine(chatId, 'U', "private text"));
    }

    [Fact]
    public void FailedResetPreservesArchiveAndDeduplicationIndex()
    {
        Assert.True(_store.AppendTruthLine("chat", 'U', "original"));
        using (File.Open(_store.GetTruthPath("chat"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(_store.TryResetTruthLog("chat", true, out _));
        }

        Assert.Single(_store.ReadTruthLines("chat"));
        Assert.False(_store.AppendTruthLine("chat", 'U', "original"));
    }

    [Fact]
    public void RequiredBackupFailureDoesNotBeginRebuild()
    {
        Assert.True(_store.AppendTruthLine("chat", 'U', "original"));
        using (File.Open(_store.GetTruthPath("chat"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(_store.TryBeginTruthRebuild("chat", true, out _, out _, CancellationToken.None));
        }

        Assert.False(_store.TryCommitTruthRebuild("chat"));
        Assert.Single(_store.ReadTruthLines("chat"));
    }

    [Fact]
    public void UnreadableArchiveDoesNotPoisonTheDeduplicationIndex()
    {
        var directory = _store.EnsureChatDir("chat");
        File.WriteAllText(Path.Combine(directory, "Truth.log"), "U|original\n");
        using (File.Open(_store.GetTruthPath("chat"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(_store.AppendTruthLine("chat", 'A', "blocked"));
        }
        Assert.False(_store.AppendTruthLine("chat", 'U', "original"));
        Assert.True(_store.AppendTruthLine("chat", 'A', "new"));
        Assert.Equal(2, _store.ReadTruthLines("chat").Length);
    }

    [Fact]
    public void EmptyRebuildCannotReplaceAnExistingArchive()
    {
        Assert.True(_store.AppendTruthLine("chat", 'U', "original"));
        Assert.True(_store.TryBeginTruthRebuild("chat", true, out _, out _, CancellationToken.None));
        Assert.False(_store.TryCommitTruthRebuild("chat"));
        Assert.Contains("original", _store.ReadTruthText("chat"));
        Assert.True(_store.AppendTruthLine("chat", 'A', "rebuilt"));
        Assert.True(_store.TryCommitTruthRebuild("chat"));
    }

    [Fact]
    public void FailedCommitCanBeRetriedWithoutLosingRebuild()
    {
        Assert.True(_store.AppendTruthLine("chat", 'U', "original"));
        Assert.True(_store.TryBeginTruthRebuild("chat", true, out _, out _, CancellationToken.None));
        Assert.True(_store.AppendTruthLine("chat", 'A', "rebuilt"));
        using (File.Open(_store.GetTruthPath("chat"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(_store.TryCommitTruthRebuild("chat"));
        }

        Assert.True(_store.TryCommitTruthRebuild("chat"));
        Assert.Equal("A|rebuilt", Assert.Single(_store.ReadTruthLines("chat")));
    }

    [Fact]
    public void BackupsAreUniqueAndResetCannotInterruptAnActiveRebuild()
    {
        Assert.True(_store.AppendTruthLine("chat", 'U', "first"));
        Assert.True(_store.TryResetTruthLog("chat", true, out var firstBackup));
        Assert.True(_store.AppendTruthLine("chat", 'U', "second"));
        Assert.True(_store.TryBeginTruthRebuild("chat", true, out var secondBackup, out _, CancellationToken.None));
        Assert.NotEqual(firstBackup, secondBackup);
        Assert.Contains("first", File.ReadAllText(firstBackup));
        Assert.Contains("second", File.ReadAllText(secondBackup));
        Assert.False(_store.TryResetTruthLog("chat", false, out _));
        _store.AbortTruthRebuild("chat");
        Assert.Contains("second", _store.ReadTruthText("chat"));
    }

    [Fact]
    public async Task ConcurrentAppendsAndCommitRetainEverySuccessfulWrite()
    {
        Assert.True(_store.TryBeginTruthRebuild("chat", false, out _, out _, CancellationToken.None));
        Assert.True(_store.AppendTruthLine("chat", 'U', "seed"));
        using var start = new ManualResetEventSlim();
        var writers = Enumerable.Range(0, 8).Select(writer => Task.Run(() =>
        {
            start.Wait();
            for (var entry = 0; entry < 40; entry++)
                Assert.True(_store.AppendTruthLine("chat", 'U', $"writer {writer} entry {entry}"));
        })).ToArray();
        var commit = Task.Run(() =>
        {
            start.Wait();
            Assert.True(_store.TryCommitTruthRebuild("chat"));
        });
        start.Set();
        await Task.WhenAll(writers.Append(commit));

        Assert.Equal(321, _store.ReadTruthLines("chat").Distinct().Count());
    }

    [Fact]
    public void TailRepairCannotTruncateAnActiveWriter()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "Truth.log");
        File.WriteAllText(path, "U|complete\nA|in flight");
        using (File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            Assert.False(TruthFile.TryRepairTruncatedTail(path, out var removed));
            Assert.Equal(0, removed);
        }

        Assert.Contains("in flight", File.ReadAllText(path));
    }

    public void Dispose()
    {
        _store.AbortAllTruthRebuilds();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
