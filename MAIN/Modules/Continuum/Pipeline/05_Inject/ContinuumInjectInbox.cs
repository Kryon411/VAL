using System.Threading.Channels;

namespace VAL.Continuum.Pipeline.Inject
{
    public sealed class ContinuumInjectInbox : IContinuumInjectInbox
    {
        public void Enqueue(EssenceInjectController.InjectSeed seed)
        {
            EssenceInjectInbox.Enqueue(seed);
        }

        public EssenceInjectController.InjectSeed? Dequeue()
        {
            return EssenceInjectInbox.Dequeue();
        }

        public bool TryDequeue(out EssenceInjectController.InjectSeed? seed)
        {
            return EssenceInjectInbox.TryDequeue(out seed);
        }

        public void Clear()
        {
            EssenceInjectInbox.Clear();
        }

        public ChannelReader<EssenceInjectController.InjectSeed> Reader => EssenceInjectInbox.Reader;

        public int Count => EssenceInjectInbox.Count;

        public void Complete()
        {
            EssenceInjectInbox.Complete();
        }
    }
}
