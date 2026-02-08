using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VAL.Host.Services;

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

        private static IServiceProvider? GetServices()
        {
            return (Application.Current as App)?.Services;
        }
    }
}
