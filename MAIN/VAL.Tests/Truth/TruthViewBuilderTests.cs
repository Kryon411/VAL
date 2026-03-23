using System;
using System.IO;
using System.Threading;
using VAL.Continuum.Pipeline.Truth;
using Xunit;

namespace VAL.Tests.Truth
{
    public sealed class TruthViewBuilderTests
    {
        [Fact]
        public void BuildViewNormalizesTruthLogAndWritesViewArtifact()
        {
            var root = Path.Combine(Path.GetTempPath(), "val-truth-view-builder", Guid.NewGuid().ToString("N"));
            var chatId = "chat-1";
            var truthPath = Path.Combine(root, chatId, TruthStore.DefaultTruthFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(truthPath)!);
            File.WriteAllText(truthPath, "U|ChatGPT said:\\nCopy code\\nHello there" + Environment.NewLine + "A|Reply");

            try
            {
                var builder = new TruthViewBuilder(new TestTruthStore(truthPath));

                var view = builder.BuildView(chatId);

                Assert.Equal(chatId, view.ChatId);
                Assert.Collection(
                    view.Messages,
                    message =>
                    {
                        Assert.Equal(TruthRole.User, message.Role);
                        Assert.Equal("Hello there", message.Text);
                        Assert.Equal(0, message.LineIndex);
                    },
                    message =>
                    {
                        Assert.Equal(TruthRole.Assistant, message.Role);
                        Assert.Equal("Reply", message.Text);
                        Assert.Equal(1, message.LineIndex);
                    });

                var viewPath = Path.Combine(Path.GetDirectoryName(truthPath)!, "Truth.view");
                Assert.True(File.Exists(viewPath));
                Assert.Contains("USER: Hello there", File.ReadAllText(viewPath));
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }

        private sealed class TestTruthStore : ITruthStore
        {
            private readonly string _truthPath;

            public TestTruthStore(string truthPath)
            {
                _truthPath = truthPath;
            }

            public string TruthFileName => TruthStore.DefaultTruthFileName;

            public bool AppendTruthLine(string chatId, char role, string text) => throw new NotSupportedException();

            public string GetChatDir(string chatId) => Path.GetDirectoryName(_truthPath)!;

            public string GetTruthPath(string chatId) => _truthPath;

            public string EnsureChatDir(string chatId)
            {
                var dir = GetChatDir(chatId);
                Directory.CreateDirectory(dir);
                return dir;
            }

            public bool TryBeginTruthRebuild(string chatId, bool backupExisting, out string backupPath, out string tempTruthPath, CancellationToken token)
                => throw new NotSupportedException();

            public void AbortTruthRebuild(string chatId) => throw new NotSupportedException();

            public bool TryCommitTruthRebuild(string chatId) => throw new NotSupportedException();
        }
    }
}
