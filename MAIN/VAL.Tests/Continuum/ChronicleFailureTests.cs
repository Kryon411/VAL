using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using VAL.Continuum;
using VAL.Continuum.Pipeline;
using VAL.Continuum.Pipeline.Truth;
using VAL.Host;
using VAL.Host.Services;
using VAL.Host.WebMessaging;
using VAL.Tests.Shared;

using Xunit;

namespace VAL.Tests.Continuum;

public sealed class ChronicleFailureTests
{
    [Theory]
    [InlineData("client_error", 4)]
    [InlineData(null, 0)]
    public async Task FailedCapturePreservesOriginalArchiveAndDerivedFiles(string? error, int turns)
    {
        using var fixture = new Fixture();
        fixture.Workflow.Start("chat", ToastReason.DockClick);
        fixture.Workflow.Complete(new ContinuumCommandMessage
        {
            ChatId = "chat",
            RequestId = fixture.Messages[0].Payload!.Value.GetProperty("requestId").GetString(),
            CapturedTurns = turns,
            Error = error,
        });

        Assert.False(fixture.Operations.IsBusy);
        Assert.Equal("U|original", Assert.Single(fixture.Store.ReadTruthLines("chat")));
        Assert.True(File.Exists(fixture.DerivedPath));
        await fixture.Background.StopAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task MissingClientCompletionTimesOutWithoutReplacingArchive()
    {
        using var fixture = new Fixture(TimeSpan.FromMilliseconds(30));
        fixture.Workflow.Start("chat", ToastReason.DockClick);
        await fixture.TimedOut.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.Background.StopAsync(TimeSpan.FromSeconds(2));

        Assert.False(fixture.Operations.IsBusy);
        Assert.False(fixture.Workflow.IsInFlight);
        Assert.Equal("U|original", Assert.Single(fixture.Store.ReadTruthLines("chat")));
        Assert.True(File.Exists(fixture.DerivedPath));
    }

    private sealed class Fixture : IToastHub, IWebMessageSender, IDisposable
    {
        private readonly TemporaryAppPaths _paths = new();
        public TruthStore Store { get; }
        public OperationCoordinator Operations { get; } = new();
        public BackgroundTaskSupervisor Background { get; } = new(new NullTestLog());
        public ContinuumChronicleWorkflow Workflow { get; }
        public string DerivedPath { get; }
        public List<MessageEnvelope> Messages { get; } = new();
        public TaskCompletionSource TimedOut { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsLaunchQuietPeriodActive => false;

        public Fixture(TimeSpan? timeout = null)
        {
            Store = new TruthStore(NullTruthTelemetryPublisher.Instance, _paths.MemoryChatsRoot);
            Store.AppendTruthLine("chat", 'U', "original");
            DerivedPath = Path.Combine(Store.GetChatDir("chat"), "Essence-M.Pulse.txt");
            File.WriteAllText(DerivedPath, "keep derived content");
            var session = new SessionContext();
            session.SetActiveChatId("chat");
            Workflow = new ContinuumChronicleWorkflow(this, this, Store, session, Operations,
                new ToastLedgerService(Store), new ContinuumArchiveService(Store, session), Background,
                action => action(), () => false, _ => { }, _ => { }, timeout);
        }

        public void Send(MessageEnvelope envelope) => Messages.Add(envelope);
        public bool TryShow(ToastKey key, string? chatId = null, bool bypassLaunchQuiet = false,
            string? titleOverride = null, string? subtitleOverride = null, string? groupKeyOverride = null,
            bool? replaceGroupOverride = null, bool? bypassBurstDedupeOverride = null, bool? oncePerChatOverride = null,
            string? ledgerIdOverride = null, ToastOrigin origin = ToastOrigin.Unknown, ToastReason reason = ToastReason.Unknown)
        {
            if (key == ToastKey.ActionUnavailable) TimedOut.TrySetResult();
            return true;
        }
        public bool TryShowActions(ToastKey key, (string Label, Action OnClick)[] actions, string? chatId = null,
            bool bypassLaunchQuiet = false, string? titleOverride = null, string? subtitleOverride = null,
            string? groupKeyOverride = null, bool? replaceGroupOverride = null, bool? oncePerChatOverride = null,
            string? ledgerIdOverride = null, ToastOrigin origin = ToastOrigin.Unknown, ToastReason reason = ToastReason.Unknown) => true;
        public void TryShowOperationCancelled(string groupKey, ToastOrigin origin = ToastOrigin.Unknown, ToastReason reason = ToastReason.Unknown) { }
        public void DismissGroup(string groupKey) { }
        public void Dispose()
        {
            Background.Dispose();
            Workflow.Dispose();
            Operations.Dispose();
            Store.AbortAllTruthRebuilds();
            _paths.Dispose();
        }
    }
}
