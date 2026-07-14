using System;
using System.IO;

using VAL.Continuum;
using VAL.Continuum.Pipeline.Truth;
using VAL.Host;

using Xunit;

namespace VAL.Tests.Continuum
{
    public sealed class ContinuumArchiveServiceTests
    {
        [Fact]
        public void ArchiveStateAndArtifactsAreManagedByStorageCollaborator()
        {
            var root = Path.Combine(Path.GetTempPath(), "VAL.Tests", Guid.NewGuid().ToString("N"));
            try
            {
                const string chatId = "archive-test";
                var truthStore = new TruthStore(NullTruthTelemetryPublisher.Instance, root);
                var sessionContext = new SessionContext();
                var service = new ContinuumArchiveService(truthStore, sessionContext);

                Assert.False(service.HasTruthLog(chatId));
                Assert.False(service.IsMeaningfulChat(chatId, capturedTurns: 3));
                Assert.True(service.IsMeaningfulChat(chatId, capturedTurns: 4));

                truthStore.AppendTruthLine(chatId, 'U', "archived content");
                Assert.True(service.HasTruthLog(chatId));

                service.WriteChronicleMarker(chatId);
                Assert.True(service.HasChronicleMarker(chatId));

                sessionContext.MarkContinuumSeeded(chatId);
                Assert.True(service.IsContinuumSeededChat(chatId));

                var chatDirectory = truthStore.EnsureChatDir(chatId);
                var derivedPath = Path.Combine(chatDirectory, "Seed.log");
                File.WriteAllText(derivedPath, "derived");
                service.TryDeleteDerivedArtifacts(chatId);
                Assert.False(File.Exists(derivedPath));

                service.TryAppendChronicleAudit(chatId, "Chronicle test");
                Assert.Contains("Chronicle test", File.ReadAllText(Path.Combine(chatDirectory, "Chronicle.audit.txt")));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }
    }
}
