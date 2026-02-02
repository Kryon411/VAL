using System.Threading;
using System.Threading.Channels;

namespace VAL.Continuum.Pipeline.Inject
{
    /// <summary>
    /// Minimal in-memory seed queue for vNext Pulse injection.
    /// Producer: QuickRefreshFlow
    /// Consumer: ContinuumPump (posts to WebView via continuum.inject_text)
    /// </summary>
    public static class EssenceInjectQueue
    {
        private static readonly Channel<EssenceInjectController.InjectSeed> Channel =
            System.Threading.Channels.Channel.CreateUnbounded<EssenceInjectController.InjectSeed>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        private static int _count;

        public static void Enqueue(EssenceInjectController.InjectSeed seed)
        {
            if (seed == null) return;
            if (string.IsNullOrWhiteSpace(seed.EssenceText)) return;

            if (Channel.Writer.TryWrite(seed))
                Interlocked.Increment(ref _count);
        }

        public static EssenceInjectController.InjectSeed? Dequeue()
        {
            return TryDequeue(out var seed) ? seed : null;
        }

        public static bool TryDequeue(out EssenceInjectController.InjectSeed? seed)
        {
            if (Channel.Reader.TryRead(out seed))
            {
                Interlocked.Decrement(ref _count);
                return true;
            }

            return false;
        }

        public static void Clear()
        {
            while (Channel.Reader.TryRead(out _))
                Interlocked.Decrement(ref _count);
        }

        public static ChannelReader<EssenceInjectController.InjectSeed> Reader => Channel.Reader;

        public static int Count => Volatile.Read(ref _count);

        public static void Complete()
        {
            Channel.Writer.TryComplete();
        }
    }
}
