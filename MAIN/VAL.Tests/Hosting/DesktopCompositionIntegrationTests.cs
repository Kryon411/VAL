using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using VAL.App.Host.Services;
using VAL.App.Host.Startup;
using VAL.App.Hosting;
using VAL.Continuum.Pipeline.Truth;
using VAL.Contracts;
using VAL.Host;
using VAL.Host.Commands;
using VAL.Host.Services;
using VAL.Host.Startup;
using VAL.Host.WebMessaging;
using VAL.Tests.Shared;

using Xunit;

namespace VAL.Tests.Hosting;

[Collection(NativeUiTestGroup.Name)]
public sealed class DesktopCompositionIntegrationTests
{
    [Fact]
    public async Task ProductionCompositionRoutesRecallAndSchedulesPrivacyCleanupWithRealStorage()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { ExerciseComposition(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(NativeUiTestGroup.InitializationTimeout);
    }

    private static void ExerciseComposition()
    {
        using var paths = new TemporaryAppPaths();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Val:DataRoot"] = paths.DataRoot,
            ["WebView:StartUrl"] = "https://chatgpt.com",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment { ContentRootPath = paths.ProductRoot });
        services.AddValDesktopApp(configuration, new StartupOptions(false, false, Array.Empty<string>()),
            new SmokeTestSettings(false, TimeSpan.FromSeconds(15), null), new StartupCrashGuard(new NullTestLog(), paths.DataRoot));
        var sender = new RecordingRuntimeSender();
        var shutdown = new ShutdownProbe();
        services.AddSingleton<IAppPaths>(paths);
        services.AddSingleton<ILog>(new NullTestLog());
        services.AddSingleton<IUiThread>(new ImmediateUi());
        services.AddSingleton<IToastHub>(new NullToastHub());
        services.AddSingleton<IWebMessageSender>(sender);
        services.AddSingleton<IProcessLauncher>(new NullProcessLauncher());
        services.AddSingleton<IDesktopDialogService>(new ConfirmedDialog());
        services.AddSingleton<IDesktopShutdownService>(shutdown);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        try
        {
            var truth = provider.GetRequiredService<ITruthStore>();
            var chatId = Guid.NewGuid().ToString();
            truth.AppendTruthLine(chatId, 'U', "integration recall question");
            truth.AppendTruthLine(chatId, 'A', "integration recall answer");
            provider.GetRequiredService<ISessionContext>().SetActiveChatId(chatId);
            var registry = provider.GetRequiredService<CommandRegistry>();
            Assert.True(Dispatch(registry, WebCommandNames.AbyssCommandSearch, chatId, new { query = "integration" }).IsAccepted);
            Assert.True(SpinWait.SpinUntil(() => sender.Messages.Any(message => message.Name == "abyss.results"), TimeSpan.FromSeconds(5)));
            var results = sender.Messages.Single(message => message.Name == "abyss.results");
            Assert.Equal(1, results.Payload!.Value.GetProperty("resultCount").GetInt32());
            Assert.True(Dispatch(registry, WebCommandNames.AbyssCommandInjectResult, chatId, new { index = 1 }).IsAccepted);
            var injection = sender.Messages.Single(message => message.Name == WebCommandNames.ContinuumInjectText);
            Assert.Contains("integration recall answer", injection.Payload!.Value.GetProperty("text").GetString());
            Assert.Equal(chatId, injection.ChatId);

            Assert.True(Dispatch(registry, WebCommandNames.PrivacyCommandWipeData, chatId, new { }).IsAccepted);
            Assert.Equal(1, shutdown.Calls);
            var settings = provider.GetRequiredService<IPrivacySettingsService>().GetSnapshot();
            Assert.False(settings.ContinuumLoggingEnabled);
            Assert.False(settings.PortalCaptureEnabled);
            Assert.True(Directory.Exists(paths.MemoryChatsRoot));
            Assert.True(File.Exists(Path.Combine(paths.StateRoot, "pending-data-wipe.json")));
        }
        finally
        {
            provider.GetRequiredService<IBackgroundTaskSupervisor>().StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        }
    }

    private static CommandDispatchResult Dispatch(CommandRegistry registry, string type, string chatId, object payload)
    {
        var command = (HostCommand)Activator.CreateInstance(typeof(HostCommand),
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object?[] { type, "{}", chatId, new Uri("https://chatgpt.com"), JsonSerializer.SerializeToElement(payload) }, null)!;
        return registry.Dispatch(command);
    }

    private sealed class ConfirmedDialog : IDesktopDialogService
    {
        public bool ConfirmWarning(string message, string caption) => true;
        public void ShowError(string message, string caption) => throw new InvalidOperationException(message);
    }
    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "VAL.Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class ShutdownProbe : IDesktopShutdownService
    {
        public int Calls { get; private set; }
        public void Shutdown() => Calls++;
    }
    private sealed class ImmediateUi : IUiThread
    {
        private readonly object _gate = new();
        public void Invoke(Action action) { lock (_gate) action(); }
        public Task InvokeAsync(Action action) { Invoke(action); return Task.CompletedTask; }
        public IDisposable StartTimer(TimeSpan interval, Action tick) => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}
