using System.Text.Json;
using VAL.Contracts;
using VAL.Continuum.Pipeline.Inject;
using VAL.Host.Commands;
using VAL.Host.WebMessaging;

namespace VAL.Host.Services.Adapters
{
    public sealed class CommandDispatcherAdapter : ICommandDispatcher
    {
        private readonly HostCommandRouter _router;

        public CommandDispatcherAdapter(HostCommandRouter router)
        {
            _router = router;
        }

        public HostCommandExecutionResult HandleWebMessageJson(WebMessageEnvelope envelope)
        {
            return _router.HandleWebMessage(envelope);
        }

        public MessageEnvelope? CreateContinuumInjectEnvelope(EssenceInjectController.InjectSeed seed)
        {
            if (seed == null || string.IsNullOrWhiteSpace(seed.EssenceText))
                return null;

            var payload = JsonSerializer.SerializeToElement(new
            {
                chatId = seed.ChatId,
                mode = seed.Mode,
                text = seed.EssenceText,
                sourceFile = seed.SourceFileName,
                essenceFile = seed.EssenceFileName,
                openNewChat = seed.OpenNewChat
            });

            return new MessageEnvelope
            {
                Type = WebMessageTypes.Command,
                Name = WebCommandNames.ContinuumInjectText,
                ChatId = seed.ChatId,
                Payload = payload
            };
        }
    }
}
