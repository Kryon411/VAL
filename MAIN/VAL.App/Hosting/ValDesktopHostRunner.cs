using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VAL.Continuum.Pipeline.Telemetry;
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
            TelemetryRegistration? telemetryRegistration = null;
            var smokeSettings = host.Services.GetRequiredService<SmokeTestSettings>();

            try
            {
                host.Start();

                var services = host.Services;
                telemetryRegistration = ConfigureTelemetry(
                    services.GetRequiredService<IToastHub>(),
                    services.GetRequiredService<TelemetryThresholdMonitor>());
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
                Shutdown(host, telemetryRegistration);
            }
        }

        private static TelemetryRegistration ConfigureTelemetry(
            IToastHub toastHub,
            TelemetryThresholdMonitor monitor)
        {
            ArgumentNullException.ThrowIfNull(toastHub);
            ArgumentNullException.ThrowIfNull(monitor);

            void OnThresholdReached(string chatId, ContinuumTelemetryThresholdLevel level)
            {
                toastHub.TryShow(
                    MapTelemetryToast(level),
                    chatId: chatId,
                    origin: ToastOrigin.Telemetry,
                    reason: ToastReason.Background);
            }

            monitor.ThresholdReached += OnThresholdReached;
            return new TelemetryRegistration(monitor, OnThresholdReached);
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

        private static void Shutdown(IHost host, TelemetryRegistration? telemetryRegistration)
        {
            try
            {
                telemetryRegistration?.Dispose();
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

        private sealed class TelemetryRegistration : IDisposable
        {
            private readonly TelemetryThresholdMonitor _monitor;
            private readonly Action<string, ContinuumTelemetryThresholdLevel> _handler;
            private bool _disposed;

            public TelemetryRegistration(
                TelemetryThresholdMonitor monitor,
                Action<string, ContinuumTelemetryThresholdLevel> handler)
            {
                _monitor = monitor;
                _handler = handler;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _monitor.ThresholdReached -= _handler;
            }
        }
    }
}
