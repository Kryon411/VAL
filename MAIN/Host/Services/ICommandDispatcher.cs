using VAL.Continuum.Pipeline.Inject;

namespace VAL.Host.Services
{
    public interface ICommandDispatcher
    {
        VAL.Host.Commands.HostCommandExecutionResult HandleWebMessageJson(VAL.Host.WebMessaging.WebMessageEnvelope envelope);
        VAL.Host.WebMessaging.MessageEnvelope? CreateContinuumInjectEnvelope(EssenceInjectController.InjectSeed seed);
    }
}
