using System.Threading;
using VAL.Continuum.Pipeline.Inject;

namespace VAL.Continuum.Pipeline.QuickRefresh
{
    internal interface IQuickRefreshService
    {
        PulseSnapshot BuildPulseSnapshot(string chatId, CancellationToken token);
        DeterministicPulseSections BuildDeterministicPulseSections(string chatId, PulseSnapshot snapshot, CancellationToken token);
        string BuildDeterministicPulsePacket(PulseSnapshot snapshot, DeterministicPulseSections deterministicSections, CancellationToken token);
        EssenceInjectController.InjectSeed CreatePulseSeed(string chatId, string pulseText, string sourceFileName, CancellationToken token);
        EssenceInjectController.InjectSeed BuildLegacyPulseSeed(string chatId, CancellationToken token);
    }
}
