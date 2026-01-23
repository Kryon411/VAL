using VAL.Continuum.Pipeline.Inject;

namespace VAL.Host.Services
{
    public interface ICommandDispatcher
    {
        void HandleWebMessage(string json);
        string? CreateContinuumInjectPayload(EssenceInjectController.InjectSeed seed);
    }
}
