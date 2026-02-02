using System.Threading.Channels;

namespace VAL.Continuum.Pipeline.Inject
{
    public interface IContinuumInjectQueue
    {
        void Enqueue(EssenceInjectController.InjectSeed seed);
        EssenceInjectController.InjectSeed? Dequeue();
        bool TryDequeue(out EssenceInjectController.InjectSeed? seed);
        void Clear();
        ChannelReader<EssenceInjectController.InjectSeed> Reader { get; }
        int Count { get; }
        void Complete();
    }
}
