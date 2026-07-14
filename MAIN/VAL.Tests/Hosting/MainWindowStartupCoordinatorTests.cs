using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using VAL.App.Host.Services;
using VAL.Host;
using VAL.Host.Options;
using VAL.Host.Startup;

using Xunit;

namespace VAL.Tests.Hosting
{
    public sealed class MainWindowStartupCoordinatorTests
    {
        [Fact]
        public async Task InitializeAsyncBootstrapsWebViewAndNavigatesConfiguredStartUrl()
        {
            var crashGuard = new FakeStartupCrashGuard();
            var dialogService = new FakeDesktopDialogService();
            var log = new FakeLog();
            var coordinator = CreateCoordinator("https://example.com/chat", crashGuard, dialogService, log);
            var webViewHost = new FakeMainWindowWebViewHost();
            var viewModelInitialized = 0;

            await coordinator.InitializeAsync(
                webViewHost,
                () =>
                {
                    viewModelInitialized++;
                    webViewHost.Focus();
                    return Task.CompletedTask;
                });

            Assert.Equal(1, webViewHost.InitializeCalls);
            Assert.Equal(1, viewModelInitialized);
            Assert.Equal(1, webViewHost.FocusCalls);
            Assert.Equal(0, dialogService.ShowErrorCalls);
            Assert.Equal(new Uri("https://example.com/chat"), webViewHost.NavigatedUri);
            Assert.Equal(System.Drawing.Color.FromArgb(11, 12, 16), webViewHost.BackgroundColor);
            Assert.Equal(1, crashGuard.MarkSuccessCalls);
            Assert.DoesNotContain(log.Warnings, warning => warning.Contains("failed", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task InitializeAsyncFallsBackToDefaultStartUrlWhenConfiguredUrlIsInvalid()
        {
            var crashGuard = new FakeStartupCrashGuard();
            var dialogService = new FakeDesktopDialogService();
            var log = new FakeLog();
            var coordinator = CreateCoordinator("not-a-valid-uri", crashGuard, dialogService, log);
            var webViewHost = new FakeMainWindowWebViewHost();

            await coordinator.InitializeAsync(
                webViewHost,
                () => Task.CompletedTask);

            Assert.Equal(new Uri(WebViewOptions.DefaultStartUrl), webViewHost.NavigatedUri);
            Assert.Contains(log.Warnings, warning => warning.Contains("Invalid StartUrl configured. Falling back to default.", StringComparison.Ordinal));
            Assert.Equal(1, crashGuard.MarkSuccessCalls);
        }

        [Fact]
        public async Task InitializeAsyncShowsErrorWhenWebViewInitializationFailsButStillStartsViewModel()
        {
            var crashGuard = new FakeStartupCrashGuard();
            var dialogService = new FakeDesktopDialogService();
            var log = new FakeLog();
            var coordinator = CreateCoordinator("https://example.com/chat", crashGuard, dialogService, log);
            var webViewHost = new FakeMainWindowWebViewHost
            {
                InitializeException = new InvalidOperationException("boom")
            };
            var viewModelInitialized = 0;

            await coordinator.InitializeAsync(
                webViewHost,
                () =>
                {
                    viewModelInitialized++;
                    return Task.CompletedTask;
                });

            Assert.Equal(1, webViewHost.InitializeCalls);
            Assert.Equal(1, viewModelInitialized);
            Assert.Equal(1, dialogService.ShowErrorCalls);
            Assert.Null(webViewHost.NavigatedUri);
            Assert.Null(webViewHost.BackgroundColor);
            Assert.Equal(1, crashGuard.MarkSuccessCalls);
            Assert.Contains(log.Warnings, warning => warning.Contains("WebView2 initialization failed:", StringComparison.Ordinal));
        }

        [Fact]
        public async Task InitializeAsyncLogsViewModelFailuresWithoutInterruptingStartup()
        {
            var crashGuard = new FakeStartupCrashGuard();
            var dialogService = new FakeDesktopDialogService();
            var log = new FakeLog();
            var coordinator = CreateCoordinator("https://example.com/chat", crashGuard, dialogService, log);
            var webViewHost = new FakeMainWindowWebViewHost();

            await coordinator.InitializeAsync(
                webViewHost,
                () => Task.FromException(new InvalidOperationException("view model failed")));

            Assert.Equal(new Uri("https://example.com/chat"), webViewHost.NavigatedUri);
            Assert.Equal(1, crashGuard.MarkSuccessCalls);
            Assert.Contains(log.Warnings, warning => warning.Contains("View model initialization failed.", StringComparison.Ordinal));
        }

        private static MainWindowStartupCoordinator CreateCoordinator(
            string startUrl,
            FakeStartupCrashGuard crashGuard,
            FakeDesktopDialogService dialogService,
            FakeLog log)
        {
            return new MainWindowStartupCoordinator(
                Options.Create(new WebViewOptions
                {
                    StartUrl = startUrl
                }),
                crashGuard,
                dialogService,
                log);
        }

        private sealed class FakeMainWindowWebViewHost : IMainWindowWebViewHost
        {
            public Exception? InitializeException { get; init; }

            public int InitializeCalls { get; private set; }

            public int FocusCalls { get; private set; }

            public Uri? NavigatedUri { get; private set; }

            public System.Drawing.Color? BackgroundColor { get; private set; }

            public Task InitializeAsync()
            {
                InitializeCalls++;
                return InitializeException == null
                    ? Task.CompletedTask
                    : Task.FromException(InitializeException);
            }

            public void ApplyDefaultBackgroundColor(System.Drawing.Color color)
            {
                BackgroundColor = color;
            }

            public void Navigate(Uri uri)
            {
                NavigatedUri = uri;
            }

            public void Focus()
            {
                FocusCalls++;
            }
        }

        private sealed class FakeStartupCrashGuard : IStartupCrashGuard
        {
            public int MarkSuccessCalls { get; private set; }

            public void MarkSuccess()
            {
                MarkSuccessCalls++;
            }
        }

        private sealed class FakeDesktopDialogService : IDesktopDialogService
        {
            public int ConfirmWarningCalls { get; private set; }
            public int ShowErrorCalls { get; private set; }

            public bool ConfirmWarning(string message, string caption)
            {
                ConfirmWarningCalls++;
                return true;
            }

            public void ShowError(string message, string caption)
            {
                ShowErrorCalls++;
            }
        }

        private sealed class FakeLog : ILog
        {
            public List<string> Infos { get; } = [];
            public List<string> Warnings { get; } = [];
            public List<string> Errors { get; } = [];
            public List<string> VerboseEntries { get; } = [];

            public void Info(string category, string message)
            {
                Infos.Add($"{category}: {message}");
            }

            public void Warn(string category, string message)
            {
                Warnings.Add($"{category}: {message}");
            }

            public void LogError(string category, string message)
            {
                Errors.Add($"{category}: {message}");
            }

            public void Verbose(string category, string message)
            {
                VerboseEntries.Add($"{category}: {message}");
            }
        }
    }
}
