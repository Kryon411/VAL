using System;
using System.Text.Json;
using VAL.Contracts;
using VAL.Host.Services;
using VAL.Host.WebMessaging;

namespace VAL.Host.Commands
{
    internal sealed class DockCommandHandlers : ICommandRegistryContributor
    {
        private readonly IDockModelService _modelService;
        private readonly IDockUiStateStore _stateStore;
        private readonly IWebMessageSender _webMessageSender;

        public DockCommandHandlers(
            IDockModelService modelService,
            IDockUiStateStore stateStore,
            IWebMessageSender webMessageSender)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            _webMessageSender = webMessageSender ?? throw new ArgumentNullException(nameof(webMessageSender));
        }

        public void Register(CommandRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            registry.Register(new CommandSpec(
                WebCommandNames.DockCommandRequestModel,
                "Dock",
                Array.Empty<string>(),
                HandleRequestModel));
            registry.Register(new CommandSpec(
                WebCommandNames.DockUiStateGet,
                "Dock",
                Array.Empty<string>(),
                HandleUiStateGet));
            registry.Register(new CommandSpec(
                WebCommandNames.DockUiStateSet,
                "Dock",
                Array.Empty<string>(),
                HandleUiStateSet));
        }

        public void HandleRequestModel(HostCommand cmd)
        {
            try
            {
                _modelService.Publish(cmd.ChatId);
            }
            catch
            {
                ValLog.Warn(nameof(DockCommandHandlers), "Failed to publish dock model.");
            }
        }

        public void HandleUiStateGet(HostCommand cmd)
        {
            try
            {
                var state = _stateStore.Load();
                var payload = JsonSerializer.SerializeToElement(state);
                _webMessageSender.Send(new MessageEnvelope
                {
                    Type = WebMessageTypes.Event,
                    Name = WebCommandNames.DockUiStateGet,
                    ChatId = cmd.ChatId,
                    Source = "host",
                    Payload = payload,
                    Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }
            catch
            {
                ValLog.Warn(nameof(DockCommandHandlers), "Failed to get dock UI state.");
            }
        }

        public void HandleUiStateSet(HostCommand cmd)
        {
            try
            {
                var current = _stateStore.Load();

                if (cmd.Root.TryGetProperty("isOpen", out var isOpenEl) && (isOpenEl.ValueKind == JsonValueKind.True || isOpenEl.ValueKind == JsonValueKind.False))
                    current.IsOpen = isOpenEl.GetBoolean();

                if (cmd.Root.TryGetProperty("x", out var xEl))
                    current.X = ReadNullableInt(xEl, current.X);

                if (cmd.Root.TryGetProperty("y", out var yEl))
                    current.Y = ReadNullableInt(yEl, current.Y);

                if (cmd.Root.TryGetProperty("w", out var wEl))
                    current.W = ReadNullableInt(wEl, current.W);

                if (cmd.Root.TryGetProperty("h", out var hEl))
                    current.H = ReadNullableInt(hEl, current.H);

                if (cmd.Root.TryGetProperty("mode", out var modeEl) && modeEl.ValueKind == JsonValueKind.String)
                    current.Mode = modeEl.GetString() ?? current.Mode;

                _stateStore.Save(current);
            }
            catch
            {
                ValLog.Warn(nameof(DockCommandHandlers), "Failed to set dock UI state.");
            }
        }

        private static int? ReadNullableInt(JsonElement element, int? current)
        {
            if (element.ValueKind == JsonValueKind.Null)
                return null;

            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var intValue))
                return intValue;

            if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed))
                return parsed;

            return current;
        }
    }
}
