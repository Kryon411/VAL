using System.Collections.Concurrent;

namespace VAL.Continuum.Pipeline.Inject
{
    /// <summary>
    /// Minimal in-memory seed queue for vNext Pulse injection.
    /// Producer: QuickRefreshFlow
    /// Consumer: MainWindow timer tick (posts to WebView via continuum.inject_text)
    /// </summary>
    public static class EssenceInjectQueue
    {
        private static readonly ConcurrentQueue<EssenceInjectController.InjectSeed> Q = new();

        public static void Enqueue(EssenceInjectController.InjectSeed seed)
        {
            if (seed == null) return;
            if (string.IsNullOrWhiteSpace(seed.EssenceText)) return;

            Q.Enqueue(seed);
        }

        public static EssenceInjectController.InjectSeed? Dequeue()
        {
            return Q.TryDequeue(out var seed) ? seed : null;
        }

        public static void Clear()
        {
            while (Q.TryDequeue(out _)) { }
        }
    }
}
