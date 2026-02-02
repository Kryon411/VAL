using System.Threading.Channels;

namespace VAL.Continuum.Pipeline.Inject
{
    public sealed class ContinuumInjectQueue : IContinuumInjectQueue
    {
        public void Enqueue(EssenceInjectController.InjectSeed seed)
        {
            EssenceInjectQueue.Enqueue(seed);
        }

        public EssenceInjectController.InjectSeed? Dequeue()
        {
            return EssenceInjectQueue.Dequeue();
        }

        public bool TryDequeue(out EssenceInjectController.InjectSeed? seed)
        {
            return EssenceInjectQueue.TryDequeue(out seed);
        }

        public void Clear()
        {
            EssenceInjectQueue.Clear();
        }

        public ChannelReader<EssenceInjectController.InjectSeed> Reader => EssenceInjectQueue.Reader;

        public int Count => EssenceInjectQueue.Count;

        public void Complete()
        {
            EssenceInjectQueue.Complete();
        }
    }
}
