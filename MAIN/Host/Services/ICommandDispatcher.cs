using VAL.Continuum.Pipeline.Inject;
using VAL.Host.WebMessaging;

namespace VAL.Host.Services
{
    public interface ICommandDispatcher
    {
        void HandleWebMessage(WebMessageEnvelope envelope);
        VAL.Host.WebMessaging.MessageEnvelope? CreateContinuumInjectEnvelope(EssenceInjectController.InjectSeed seed);
    }
}
