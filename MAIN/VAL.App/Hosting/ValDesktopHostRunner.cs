using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VAL.Continuum.Pipeline.Telemetry;
using VAL.Continuum.Pipeline.Truth;
using VAL.Host;
using VAL.Host.Options;
using VAL.Host.Services;
using VAL.Host.Startup;

namespace VAL.Hosting
{
    public static class ValDesktopHostRunner
    {
        public static int RunValDesktopApp(
            this IHost host,
            SafeBoot safeBoot,
            StartupOptions startupOptions)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(safeBoot);
            ArgumentNullException.ThrowIfNull(startupOptions);

            SmokeTestState? smokeState = null;
            var smokeSettings = host.Services.GetRequiredService<SmokeTestSettings>();

            try
            {
                host.Start();

                var services = host.Services;
                ConfigureTelemetry(services.GetRequiredService<IToastHub>());
                LogStartup(safeBoot, services, startupOptions);

                var app = services.GetRequiredService<App>();
                var crashHandler = services.GetRequiredService<ICrashHandler>();
                crashHandler.Register(app);

                if (smokeSettings.Enabled)
                {
                    var smokeRunner = services.GetRequiredService<SmokeTestRunner>();
                    smokeState = services.GetRequiredService<SmokeTestState>();
                    smokeRunner.Register(app, smokeState);
                }

                app.Run();

                if (smokeSettings.Enabled && smokeState != null)
                    return smokeState.Completion.Task.GetAwaiter().GetResult();

                return Environment.ExitCode;
            }
            finally
            {
                Shutdown(host);
            }
        }

        private static void ConfigureTelemetry(IToastHub toastHub)
        {
            TruthTelemetryBridge.Configure(TelemetryThresholdMonitor.UpdateFromTruthBytes);
            TelemetryThresholdMonitor.Configure((chatId, level) =>
            {
                toastHub.TryShow(
                    MapTelemetryToast(level),
                    chatId: chatId,
                    origin: ToastOrigin.Telemetry,
                    reason: ToastReason.Background);
            });
        }

        private static void LogStartup(
            SafeBoot safeBoot,
            IServiceProvider services,
            StartupOptions startupOptions)
        {
            var appPaths = services.GetRequiredService<IAppPaths>();
            var buildInfo = services.GetRequiredService<IBuildInfo>();
            var webViewOptions = services.GetRequiredService<IOptions<WebViewOptions>>().Value;

            safeBoot.LogStartupInfo(buildInfo, appPaths, webViewOptions);
            if (startupOptions.SafeMode)
            {
                ValLog.Info("Startup", "SAFE MODE: modules disabled");
            }
        }

        private static void Shutdown(IHost host)
        {
            try
            {
                TruthTelemetryBridge.Configure(null);
                TelemetryThresholdMonitor.Configure(null);
                host.StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore shutdown errors.
            }
        }

        private static ToastKey MapTelemetryToast(ContinuumTelemetryThresholdLevel level)
        {
            return level switch
            {
                ContinuumTelemetryThresholdLevel.Early => ToastKey.TelemetrySessionSizeEarly,
                ContinuumTelemetryThresholdLevel.Large => ToastKey.TelemetrySessionSizeLarge,
                ContinuumTelemetryThresholdLevel.VeryLarge => ToastKey.TelemetrySessionSizeVeryLarge,
                _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
            };
        }
    }
}
