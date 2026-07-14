using System.Text;

using VAL.Continuum.Pipeline;
using VAL.Continuum.Pipeline.Truth;
using VAL.Host;
using VAL.Host.Services;

namespace VAL.Continuum
{
    internal sealed class ContinuumArchiveService
    {
        private const long NonTrivialTruthLength = 2048;
        private readonly ITruthStore _truthStore;
        private readonly ISessionContext _sessionContext;

        public ContinuumArchiveService(ITruthStore truthStore, ISessionContext sessionContext)
        {
            _truthStore = truthStore ?? throw new ArgumentNullException(nameof(truthStore));
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
        }

        public bool HasTruthLog(string chatId)
        {
            return GetTruthLength(chatId) > 4;
        }

        public bool HasNonTrivialTruthLog(string chatId)
        {
            return GetTruthLength(chatId) >= NonTrivialTruthLength;
        }

        public bool HasChronicleMarker(string chatId)
        {
            try
            {
                return File.Exists(GetChronicleMarkerPath(chatId));
            }
            catch
            {
                return false;
            }
        }

        public void WriteChronicleMarker(string chatId)
        {
            try
            {
                var markerPath = GetChronicleMarkerPath(chatId);
                if (!File.Exists(markerPath))
                    File.WriteAllText(markerPath, DateTime.UtcNow.ToString("O"));
            }
            catch
            {
                // Marker creation is best-effort; the rebuilt Truth log remains authoritative.
            }
        }

        public bool IsMeaningfulChat(string chatId, int capturedTurns)
        {
            return capturedTurns > 0
                ? capturedTurns >= 4
                : GetTruthLength(chatId) >= NonTrivialTruthLength;
        }

        public bool IsContinuumSeededChat(string chatId)
        {
            try
            {
                if (_sessionContext.GetOrigin(chatId) == ChatOrigin.ContinuumSeeded)
                    return true;
            }
            catch
            {
                // Fall back to the archive when session metadata is unavailable.
            }

            try
            {
                var truthPath = _truthStore.GetTruthPath(chatId);
                if (!File.Exists(truthPath))
                    return false;

                var content = new StringBuilder();
                foreach (var entry in TruthReader.Read(truthPath, repairTailFirst: true))
                {
                    if (content.Length >= 32768)
                        break;

                    content.Append(entry.Role);
                    content.Append('|');
                    content.Append(entry.Payload);
                    content.Append('\n');
                }

                return content.Length > 0 && ContinuumSeedClassifier.IsContinuumSeed(content.ToString());
            }
            catch
            {
                return false;
            }
        }

        public void TryAbortTruthRebuild(string chatId)
        {
            try
            {
                _truthStore.AbortTruthRebuild(chatId);
            }
            catch
            {
                // Operation state is released separately.
            }
        }

        public void TryDeleteDerivedArtifacts(string chatId)
        {
            try
            {
                var directory = _truthStore.EnsureChatDir(chatId);
                foreach (var fileName in DerivedArtifactNames)
                {
                    try
                    {
                        var path = Path.Combine(directory, fileName);
                        if (File.Exists(path))
                            File.Delete(path);
                    }
                    catch
                    {
                        // Each derived artifact is independent cleanup.
                    }
                }
            }
            catch
            {
                // Rebuild may continue even when old derived artifacts cannot be removed.
            }
        }

        public void TryAppendChronicleAudit(string chatId, string line)
        {
            try
            {
                var directory = _truthStore.EnsureChatDir(chatId);
                var path = Path.Combine(directory, "Chronicle.audit.txt");
                AtomicFile.TryAppendAllText(path, line.Trim() + Environment.NewLine, durable: false);
            }
            catch
            {
                // Auditing must not change Chronicle completion behavior.
            }
        }

        private static readonly string[] DerivedArtifactNames =
        {
            "Truth.view",
            "Seed.log",
            "RestructuredSeed.log",
            "Essence-M.Pulse.txt",
        };

        private long GetTruthLength(string chatId)
        {
            try
            {
                var truthPath = _truthStore.GetTruthPath(chatId);
                return File.Exists(truthPath) ? new FileInfo(truthPath).Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        private string GetChronicleMarkerPath(string chatId)
        {
            return Path.Combine(_truthStore.EnsureChatDir(chatId), "Chronicle.complete.flag");
        }
    }
}
