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

        public void Clear()
        {
            EssenceInjectQueue.Clear();
        }
    }
}
