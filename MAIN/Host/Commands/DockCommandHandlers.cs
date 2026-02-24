using System;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VAL.Contracts;
using VAL.Host.Services;
using VAL.Host.WebMessaging;

namespace VAL.Host.Commands
{
    internal static class DockCommandHandlers
    {
        public static void HandleRequestModel(HostCommand cmd)
        {
            try
            {
                var services = GetServices();
                if (services == null)
                    return;

                var modelService = services.GetRequiredService<IDockModelService>();
                modelService.Publish(cmd.ChatId);
            }
            catch
            {
                ValLog.Warn(nameof(DockCommandHandlers), "Failed to publish dock model.");
            }
        }

        public static void HandleUiStateGet(HostCommand cmd)
        {
            try
            {
                var services = GetServices();
                if (services == null)
                    return;

                var store = services.GetRequiredService<IDockUiStateStore>();
                var sender = services.GetRequiredService<IWebMessageSender>();
                var state = store.Load();
                var payload = JsonSerializer.SerializeToElement(state);
                sender.Send(new MessageEnvelope
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

        public static void HandleUiStateSet(HostCommand cmd)
        {
            try
            {
                var services = GetServices();
                if (services == null)
                    return;

                var store = services.GetRequiredService<IDockUiStateStore>();
                var current = store.Load();

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

                store.Save(current);
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

        private static IServiceProvider? GetServices()
        {
            return (Application.Current as App)?.Services;
        }
    }
}
