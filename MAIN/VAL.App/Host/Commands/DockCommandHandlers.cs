using System;
using System.Globalization;
using System.Text.Json;

using VAL.App.Host.Services;
using VAL.App.State;
using VAL.Contracts;
using VAL.Host.Services;
using VAL.Host.WebMessaging;

namespace VAL.App.Host.Commands
{
    internal sealed class DockCommandHandlers : ICommandRegistryContributor
    {
        private const int DockUiStateVersion = 1;
        private const string DockUiMode = "shelf";
        private readonly IDockModelService _modelService;
        private readonly IControlCentreUiStateStore _stateStore;
        private readonly IWebMessageSender _webMessageSender;
        private readonly ILog _log;

        public DockCommandHandlers(
            IDockModelService modelService,
            IControlCentreUiStateStore stateStore,
            IWebMessageSender webMessageSender,
            ILog log)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            _webMessageSender = webMessageSender ?? throw new ArgumentNullException(nameof(webMessageSender));
            _log = log ?? throw new ArgumentNullException(nameof(log));
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
                _log.Warn(nameof(DockCommandHandlers), "Failed to publish dock model.");
            }
        }

        public void HandleUiStateGet(HostCommand cmd)
        {
            try
            {
                var state = _stateStore.Load();
                var payload = JsonSerializer.SerializeToElement(new
                {
                    version = DockUiStateVersion,
                    isOpen = state.Dock.IsOpen,
                    x = (int)Math.Round(state.Dock.X),
                    y = (int)Math.Round(state.Dock.Y),
                    w = (int)Math.Round(state.Dock.W),
                    h = (int)Math.Round(state.Dock.H),
                    mode = DockUiMode,
                    updatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                });
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
                _log.Warn(nameof(DockCommandHandlers), "Failed to get dock UI state.");
            }
        }

        public void HandleUiStateSet(HostCommand cmd)
        {
            try
            {
                var current = _stateStore.Load();
                var dock = current.Dock;

                if (cmd.TryGetBool("isOpen", out var isOpen))
                    dock.IsOpen = isOpen;

                if (cmd.TryGetInt("x", out var x))
                    dock.X = x;

                if (cmd.TryGetInt("y", out var y))
                    dock.Y = y;

                if (cmd.TryGetInt("w", out var w))
                    dock.W = w;

                if (cmd.TryGetInt("h", out var h))
                    dock.H = h;

                current.Dock = dock;
                _stateStore.Save(current);
            }
            catch
            {
                _log.Warn(nameof(DockCommandHandlers), "Failed to set dock UI state.");
            }
        }
    }
}
