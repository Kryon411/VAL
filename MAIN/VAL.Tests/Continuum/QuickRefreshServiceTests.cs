using System;
using System.IO;
using System.Threading;
using VAL.Continuum.Pipeline;
using VAL.Continuum.Pipeline.QuickRefresh;
using VAL.Continuum.Pipeline.Truth;
using Xunit;

namespace VAL.Tests.Continuum
{
    public sealed class QuickRefreshServiceTests
    {
        [Fact]
        public void BuildPulseSnapshotUsesInjectedTruthViewBuilderAndWritesSeedLog()
        {
            var root = Path.Combine(Path.GetTempPath(), "val-quick-refresh-service", Guid.NewGuid().ToString("N"));
            var chatId = "chat-1";
            var truthPath = Path.Combine(root, chatId, TruthStore.DefaultTruthFileName);

            try
            {
                var service = new QuickRefreshService(
                    new TestTruthStore(truthPath),
                    new StubTruthViewBuilder(BuildTruthView(chatId)));

                var snapshot = service.BuildPulseSnapshot(chatId, CancellationToken.None);

                Assert.Equal(chatId, snapshot.ChatId);
                Assert.NotEmpty(snapshot.Filter1Exchanges);
                Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(truthPath)!, "Seed.log")));
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }

        private static TruthView BuildTruthView(string chatId)
        {
            return new TruthView
            {
                ChatId = chatId,
                Messages = new[]
                {
                    new TruthMessage
                    {
                        Role = TruthRole.User,
                        LineIndex = 0,
                        Text = "We need Continuum to own final Pulse packet rendering."
                    },
                    new TruthMessage
                    {
                        Role = TruthRole.Assistant,
                        LineIndex = 1,
                        Text = "We'll move the final packet shape into Continuum and keep Signal focused on semantic output."
                    },
                    new TruthMessage
                    {
                        Role = TruthRole.User,
                        LineIndex = 2,
                        Text = "Task: Refactor VAL Pulse so Continuum owns the final handoff packet."
                    },
                    new TruthMessage
                    {
                        Role = TruthRole.Assistant,
                        LineIndex = 3,
                        Text = "Proceed by adding a frozen PulseSnapshot, deterministic sections, and a local PulsePacketComposer."
                    },
                    new TruthMessage
                    {
                        Role = TruthRole.User,
                        LineIndex = 4,
                        Text = "Keep the final payload plain text first, preserve stable headings, and keep the injection runtime unchanged."
                    },
                    new TruthMessage
                    {
                        Role = TruthRole.Assistant,
                        LineIndex = 5,
                        Text = "The host can build a deterministic fallback packet first, then ask Signal only for semantic summary bullets."
                    }
                }
            };
        }

        private sealed class StubTruthViewBuilder : ITruthViewBuilder
        {
            private readonly TruthView _truthView;

            public StubTruthViewBuilder(TruthView truthView)
            {
                _truthView = truthView;
            }

            public TruthView BuildView(string chatId) => _truthView;
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
