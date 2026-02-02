namespace VAL.Continuum.Pipeline.Inject
{
    public interface IContinuumInjectQueue
    {
        void Enqueue(EssenceInjectController.InjectSeed seed);
        EssenceInjectController.InjectSeed? Dequeue();
        void Clear();
    }
}
