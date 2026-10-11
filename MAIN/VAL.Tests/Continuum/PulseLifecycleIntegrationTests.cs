using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using VAL.Continuum;
using VAL.Continuum.Pipeline;
using VAL.Continuum.Pipeline.Inject;
using VAL.Continuum.Pipeline.QuickRefresh;
using VAL.Continuum.Pipeline.Truth;
using VAL.Contracts;
using VAL.Host;
using VAL.Host.Services;
using VAL.Tests.Shared;

using Xunit;

namespace VAL.Tests.Continuum;

public sealed class PulseLifecycleIntegrationTests
{
    [Fact]
    public async Task CancellationBeforeWatcherSchedulingStillReleasesOperation()
    {
        var tasks = new DeferredBackgroundTasks();
        using var fixture = new Fixture(TimeSpan.FromMinutes(1), tasks);
        fixture.StartAndFlush();
        var signal = fixture.Inbox.Dequeue()!;
        fixture.Operations.RequestCancel();

        await tasks.RunScheduledAsync("Pulse lifetime");

        Assert.False(fixture.Workflow.IsInFlight);
        Assert.False(fixture.Operations.IsBusy);
        Assert.True(signal.CancellationToken.IsCancellationRequested);
        Assert.Contains(fixture.Sender.Messages, message => message.Name == WebCommandNames.ContinuumInjectCancel &&
            message.Payload!.Value.GetProperty("requestId").GetString() == signal.RequestId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeadlineAndCancellationEndOperationAndInvalidateQueuedSeeds(bool cancel)
    {
        using var fixture = new Fixture(cancel ? TimeSpan.FromMinutes(1) : TimeSpan.FromMilliseconds(200));
        fixture.StartAndFlush();
        var signal = fixture.Inbox.Dequeue()!;
        Assert.Equal("Signal", signal.Mode);
        if (cancel) fixture.Operations.RequestCancel();
        await WaitUntilAsync(() => !fixture.Workflow.IsInFlight && !fixture.Operations.IsBusy &&
            fixture.Sender.Messages.Any(message => message.Name == WebCommandNames.ContinuumInjectCancel &&
                message.Payload!.Value.GetProperty("requestId").GetString() == signal.RequestId));
        Assert.False(fixture.Operations.IsBusy);
        Assert.True(signal.CancellationToken.IsCancellationRequested);
        Assert.Contains(fixture.Sender.Messages, message => message.Name == WebCommandNames.ContinuumInjectCancel &&
            message.Payload!.Value.GetProperty("requestId").GetString() == signal.RequestId);
        await fixture.Tasks.StopAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task OnlyExactFinalPulseRequestCanCompleteOrFailRefresh()
    {
        using var fixture = new Fixture(TimeSpan.FromMinutes(1));
        fixture.StartAndFlush();
        var signal = fixture.Inbox.Dequeue()!;
        fixture.Workflow.HandleEvent(new ContinuumCommandMessage
        {
            ChatId = fixture.ChatId,
            RequestId = signal.RequestId,
            Event = "signal.send.failed:no_button",
        });
        var final = fixture.Inbox.Dequeue()!;
        foreach (var requestId in new[] { "", "older-request", signal.RequestId })
        {
            fixture.Workflow.HandleEvent(new ContinuumCommandMessage
            {
                ChatId = fixture.ChatId,
                RequestId = requestId,
                Event = "refresh.inject.success:Pulse:new_chat",
            });
            fixture.Workflow.HandleEvent(new ContinuumCommandMessage
            {
                ChatId = fixture.ChatId,
                RequestId = requestId,
                Event = "refresh.inject.failed:timeout",
            });
            Assert.True(fixture.Workflow.IsInFlight);
        }
        fixture.Workflow.HandleEvent(new ContinuumCommandMessage
        {
            ChatId = fixture.ChatId,
            RequestId = final.RequestId,
            Event = "refresh.inject.success:Quick:current_chat",
        });
        Assert.True(fixture.Workflow.IsInFlight);
        fixture.Workflow.HandleEvent(new ContinuumCommandMessage
        {
            ChatId = Guid.NewGuid().ToString(),
            RequestId = final.RequestId,
            Event = "refresh.inject.success:Pulse:new_chat",
        });
        Assert.False(fixture.Workflow.IsInFlight);
        Assert.True(final.CancellationToken.IsCancellationRequested);
        await fixture.Tasks.StopAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(condition(), "Pulse lifecycle transition did not finish.");
    }

    private sealed class Fixture : IDisposable
    {
        public TemporaryAppPaths Paths { get; } = new();
        public string ChatId { get; } = Guid.NewGuid().ToString();
        public OperationCoordinator Operations { get; } = new();
        public BackgroundTaskSupervisor Tasks { get; } = new(new NullTestLog());
        public RecordingRuntimeSender Sender { get; } = new();
        public ContinuumInjectInbox Inbox { get; } = new();
        public TruthStore Truth { get; }
        public ContinuumPulseWorkflow Workflow { get; }
        public Fixture(TimeSpan timeout, IBackgroundTaskSupervisor? backgroundTasks = null)
        {
            Truth = new TruthStore(NullTruthTelemetryPublisher.Instance, Paths.MemoryChatsRoot);
            Truth.AppendTruthLine(ChatId, 'U', "content");
            var session = new SessionContext();
            session.SetActiveChatId(ChatId);
            Workflow = new ContinuumPulseWorkflow(Sender, new NullToastHub(), new QuickRefresh(), Inbox, session,
                Operations, backgroundTasks ?? Tasks, new ContinuumArchiveService(Truth, session), () => false, action => action(), timeout);
        }
        public void StartAndFlush()
        {
            Workflow.Start(ChatId, ToastReason.DockClick);
            var flush = Sender.Messages.Single(message => message.Name == WebCommandNames.ContinuumCaptureFlush);
            Workflow.HandleCaptureFlushAck(new ContinuumCommandMessage
            {
                ChatId = ChatId,
                RequestId = flush.Payload!.Value.GetProperty("requestId").GetString(),
            });
        }
        public void Dispose()
        {
            Workflow.Dispose();
            Tasks.Dispose();
            Operations.Dispose();
            Truth.Dispose();
            Paths.Dispose();
        }
    }

    private sealed class DeferredBackgroundTasks : IBackgroundTaskSupervisor
    {
        private readonly Dictionary<string, (Func<CancellationToken, Task> Work, CancellationToken Token)> _scheduled = new();
        public int ActiveCount => _scheduled.Count;
        public void Run(string operation, Func<CancellationToken, Task> work, Action<Exception>? onError = null,
            CancellationToken cancellationToken = default) => _scheduled.Add(operation, (work, cancellationToken));
        public Task StopAsync(TimeSpan timeout) => Task.CompletedTask;
        public async Task RunScheduledAsync(string operation)
        {
            var scheduled = _scheduled[operation];
            // Match Task.Run's behavior when its scheduling token is already cancelled.
            if (!scheduled.Token.IsCancellationRequested) await scheduled.Work(scheduled.Token);
        }
    }

    private sealed class QuickRefresh : IQuickRefreshService
    {
        public PulseSnapshot BuildPulseSnapshot(string chatId, System.Threading.CancellationToken token) => new() { ChatId = chatId };
        public DeterministicPulseSections BuildDeterministicPulseSections(string chatId, PulseSnapshot snapshot,
            System.Threading.CancellationToken token) => new();
        public string BuildDeterministicPulsePacket(PulseSnapshot snapshot, DeterministicPulseSections sections,
            System.Threading.CancellationToken token) => "packet";
        public EssenceInjectController.InjectSeed CreatePulseSeed(string chatId, string text, string source,
            System.Threading.CancellationToken token) => new() { ChatId = chatId, EssenceText = text };
        public EssenceInjectController.InjectSeed BuildLegacyPulseSeed(string chatId, System.Threading.CancellationToken token) =>
            CreatePulseSeed(chatId, "packet", "test", token);
    }
}
