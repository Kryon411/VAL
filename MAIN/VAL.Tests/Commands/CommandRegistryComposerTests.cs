using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using VAL.Contracts;
using VAL.Continuum;
using VAL.Host;
using VAL.Host.Abyss;
using VAL.Host.Commands;
using VAL.Host.Services;
using VAL.Host.WebMessaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Xunit;

namespace VAL.Tests.Commands
{
    public sealed class CommandRegistryComposerTests
    {
        private static readonly Uri TestSourceUri = new("https://chatgpt.com");

        [Fact]
        public void RegisterBuildsRegistryWithoutThrowing()
        {
            var registry = new CommandRegistry();
            var composer = CreateComposer();

            var ex = Record.Exception(() => composer.Register(registry));

            Assert.Null(ex);
        }

        [Fact]
        public void RegisterAcceptsNavigationCommand()
        {
            var registry = new CommandRegistry();
            var navigationRuntime = new FakeWebViewRuntime();
            var composer = CreateComposer(navigationRuntime);
            composer.Register(registry);

            using var doc = JsonDocument.Parse("{}");
            var command = CreateHostCommand(WebCommandNames.NavCommandGoChat, doc.RootElement);

            var result = registry.Dispatch(command);

            Assert.Equal(CommandDispatchStatus.Accepted, result.Status);
            Assert.True(result.IsAccepted);
            Assert.Equal("https://chatgpt.com/", navigationRuntime.LastNavigatedUrl);
        }

        private static CommandRegistryComposer CreateComposer(FakeWebViewRuntime? webViewRuntime = null)
        {
            var contributors = new ICommandRegistryContributor[]
            {
                new ContinuumCommandHandlers(CreateUninitialized<ContinuumHost>(), new FakeLog()),
                new VoidCommandHandlers(new FakeToastHub()),
                new PortalCommandHandlers(new FakePortalRuntimeStateManager(), new FakeLog()),
                new PrivacyCommandHandlers(
                    new FakePrivacySettingsService(),
                    new FakeAppPaths(),
                    new FakeProcessLauncher(),
                    new FakeDataWipeService(),
                    new FakeToastHub(),
                    new FakeWebMessageSender(),
                    new FakeLog()),
                new ToolsCommandHandlers(
                    new FakeUiThread(),
                    new FakeTruthHealthWindowService(),
                    new FakeDiagnosticsWindowService(),
                    new FakeCommandDiagnosticsReporter(),
                    new FakeLog()),
                new NavigationCommandHandlers(webViewRuntime ?? new FakeWebViewRuntime(), new FakeToastHub()),
                new DockCommandHandlers(new FakeDockModelService(), new FakeDockUiStateStore(), new FakeWebMessageSender(), new FakeLog()),
                new AbyssCommandHandlers(CreateUninitialized<AbyssRuntime>(), new FakeLog()),
            };

            return new CommandRegistryComposer(contributors);
        }

        private static HostCommand CreateHostCommand(string type, JsonElement root)
        {
            var created = Activator.CreateInstance(
                typeof(HostCommand),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: new object?[] { type, "{}", null, TestSourceUri, root },
                culture: null);

            return Assert.IsType<HostCommand>(created);
        }

        private static T CreateUninitialized<T>() where T : class
        {
            return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        }

        private sealed class FakeToastHub : IToastHub
        {
            public bool IsLaunchQuietPeriodActive => false;

            public bool TryShow(
                ToastKey key,
                string? chatId = null,
                bool bypassLaunchQuiet = false,
                string? titleOverride = null,
                string? subtitleOverride = null,
                string? groupKeyOverride = null,
                bool? replaceGroupOverride = null,
                bool? bypassBurstDedupeOverride = null,
                bool? oncePerChatOverride = null,
                string? ledgerIdOverride = null,
                ToastOrigin origin = ToastOrigin.Unknown,
                ToastReason reason = ToastReason.Unknown)
            {
                return true;
            }

            public bool TryShowActions(
                ToastKey key,
                (string Label, Action OnClick)[] actions,
                string? chatId = null,
                bool bypassLaunchQuiet = false,
                string? titleOverride = null,
                string? subtitleOverride = null,
                string? groupKeyOverride = null,
                bool? replaceGroupOverride = null,
                bool? oncePerChatOverride = null,
                string? ledgerIdOverride = null,
                ToastOrigin origin = ToastOrigin.Unknown,
                ToastReason reason = ToastReason.Unknown)
            {
                return true;
            }

            public void TryShowOperationCancelled(
                string groupKey,
                ToastOrigin origin = ToastOrigin.Unknown,
                ToastReason reason = ToastReason.Unknown)
            {
            }

            public void DismissGroup(string groupKey)
            {
            }
        }

        private sealed class FakePortalRuntimeStateManager : IPortalRuntimeStateManager
        {
            public void SetEnabled(bool enabled) { }
            public void SetPrivacyAllowed(bool allowed) { }
            public void ClearStaging() { }
            public void OpenSnipOverlay() { }
            public void SendStaged(int max) { }
        }

        private sealed class FakePrivacySettingsService : IPrivacySettingsService
        {
            public event Action<PrivacySettingsSnapshot>? SettingsChanged;

            public PrivacySettingsSnapshot GetSnapshot() => new(1, true, true);

            public bool UpdateContinuumLogging(bool enabled)
            {
                SettingsChanged?.Invoke(new PrivacySettingsSnapshot(1, enabled, true));
                return true;
            }

            public bool UpdatePortalCapture(bool enabled)
            {
                SettingsChanged?.Invoke(new PrivacySettingsSnapshot(1, true, enabled));
                return true;
            }
        }

        private sealed class FakeAppPaths : IAppPaths
        {
            public string ContentRoot => "C:\\VAL";
            public string ProductRoot => "C:\\VAL";
            public string StateRoot => "C:\\VAL\\State";
            public string DataRoot => "C:\\VAL\\Data";
            public string LogsRoot => "C:\\VAL\\Logs";
            public string ModulesRoot => "C:\\VAL\\Modules";
            public string MemoryChatsRoot => "C:\\VAL\\Memory\\Chats";
            public string ProfileRoot => "C:\\VAL\\Profile";
        }

        private sealed class FakeProcessLauncher : IProcessLauncher
        {
            public void OpenFolder(string path) { }
            public void OpenUrl(string url) { }
            public void OpenPath(string path) { }
        }

        private sealed class FakeDataWipeService : IDataWipeService
        {
            public DataWipeResult WipeData() => new(true, false, 0, 0);
        }

        private sealed class FakeWebMessageSender : IWebMessageSender
        {
            public void Send(MessageEnvelope envelope) { }
        }

        private sealed class FakeUiThread : IUiThread
        {
            public void Invoke(Action action) => action();

            public System.Threading.Tasks.Task InvokeAsync(Action action)
            {
                action();
                return System.Threading.Tasks.Task.CompletedTask;
            }

            public IDisposable StartTimer(TimeSpan interval, Action tick) => new FakeDisposable();
        }

        private sealed class FakeTruthHealthWindowService : ITruthHealthWindowService
        {
            public void ShowTruthHealth() { }
        }

        private sealed class FakeDiagnosticsWindowService : IDiagnosticsWindowService
        {
            public void ShowDiagnostics() { }
        }

        private sealed class FakeCommandDiagnosticsReporter : ICommandDiagnosticsReporter
        {
            public void ReportDiagnosticsFailure(HostCommand? cmd, Exception? exception, string reason) { }
        }

        private sealed class FakeLog : ILog
        {
            public void Info(string category, string message) { }
            public void Warn(string category, string message) { }
            public void LogError(string category, string message) { }
            public void Verbose(string category, string message) { }
        }

        private sealed class FakeWebViewRuntime : IWebViewRuntime
        {
            public string? LastNavigatedUrl { get; private set; }
            public CoreWebView2? Core => null;
            public Uri? LastChatUri => null;
            public event Action<WebMessageEnvelope>? WebMessageJsonReceived { add { } remove { } }
            public event Action? NavigationCompleted { add { } remove { } }

            public System.Threading.Tasks.Task InitializeAsync(WebView2 control) => System.Threading.Tasks.Task.CompletedTask;
            public void PostJson(string json) { }
            public System.Threading.Tasks.Task ExecuteScriptAsync(string js) => System.Threading.Tasks.Task.CompletedTask;
            public void Navigate(string url) => LastNavigatedUrl = url;
            public bool TryGoBack() => false;
        }

        private sealed class FakeDockModelService : IDockModelService
        {
            public void Publish(string? chatId = null) { }
            public void UpdatePortalState(bool enabled, bool privacyAllowed, int count) { }
        }

        private sealed class FakeDockUiStateStore : IDockUiStateStore
        {
            public DockUiState Load() => DockUiState.Default;
            public void Save(DockUiState state) { }
        }

        private sealed class FakeDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
