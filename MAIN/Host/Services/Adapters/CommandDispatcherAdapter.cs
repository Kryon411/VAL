using System.Text.Json;
using VAL.Continuum.Pipeline.Inject;
using VAL.Host.Commands;

namespace VAL.Host.Services.Adapters
{
    public sealed class CommandDispatcherAdapter : ICommandDispatcher
    {
        public void HandleWebMessage(string json)
        {
            HostCommandRouter.Handle(json);
        }

        public string? CreateContinuumInjectPayload(EssenceInjectController.InjectSeed seed)
        {
            if (seed == null || string.IsNullOrWhiteSpace(seed.EssenceText))
                return null;

            var payload = new
            {
                type = "continuum.inject_text",
                chatId = seed.ChatId,
                mode = seed.Mode,
                text = seed.EssenceText,
                sourceFile = seed.SourceFileName,
                essenceFile = seed.EssenceFileName,
                openNewChat = seed.OpenNewChat
            };

            return JsonSerializer.Serialize(payload);
        }
    }
}
