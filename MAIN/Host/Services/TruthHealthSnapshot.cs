using VAL.Continuum.Pipeline.Truth;

namespace VAL.Host.Services
{
    internal sealed record TruthHealthSnapshot(
        bool HasChat,
        string ChatId,
        string RelativePath,
        TruthHealthReport? Report);
}
