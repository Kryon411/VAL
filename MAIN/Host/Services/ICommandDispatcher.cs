using VAL.Continuum.Pipeline.Inject;

namespace VAL.Host.Services
{
    public interface ICommandDispatcher
    {
        void HandleWebMessageJson(string json);
        VAL.Host.WebMessaging.MessageEnvelope? CreateContinuumInjectEnvelope(EssenceInjectController.InjectSeed seed);
    }
}
