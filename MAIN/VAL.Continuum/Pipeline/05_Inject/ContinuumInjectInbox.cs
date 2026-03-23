using System.Threading.Channels;
using System.Threading;

namespace VAL.Continuum.Pipeline.Inject
{
    public sealed class ContinuumInjectInbox : IContinuumInjectInbox
    {
        private readonly Channel<EssenceInjectController.InjectSeed> _channel =
            Channel.CreateUnbounded<EssenceInjectController.InjectSeed>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        private int _count;

        public void Enqueue(EssenceInjectController.InjectSeed seed)
        {
            if (seed == null) return;
            if (string.IsNullOrWhiteSpace(seed.EssenceText)) return;

            if (_channel.Writer.TryWrite(seed))
                Interlocked.Increment(ref _count);
        }

        public EssenceInjectController.InjectSeed? Dequeue()
        {
            return TryDequeue(out var seed) ? seed : null;
        }

        public bool TryDequeue(out EssenceInjectController.InjectSeed? seed)
        {
            if (_channel.Reader.TryRead(out seed))
            {
                Interlocked.Decrement(ref _count);
                return true;
            }

            return false;
        }

        public void Clear()
        {
            while (_channel.Reader.TryRead(out _))
                Interlocked.Decrement(ref _count);
        }

        public ChannelReader<EssenceInjectController.InjectSeed> Reader => _channel.Reader;

        public int Count => Volatile.Read(ref _count);

        public void Complete()
        {
            _channel.Writer.TryComplete();
        }
    }
}
