using System;
using System.Collections.Generic;
using System.IO;
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
using VAL.Host.WebMessaging;

using Xunit;

namespace VAL.Tests.Continuum
{
    public sealed class ContinuumChronicleWorkflowTests
    {
        [Fact]
        public void StartAndCompleteOwnTheChronicleOperationLifecycle()
        {
            var root = Path.Combine(Path.GetTempPath(), "VAL.Tests", Guid.NewGuid().ToString("N"));
            try
            {
                const string chatId = "chronicle-workflow";
                var truthStore = new TruthStore(NullTruthTelemetryPublisher.Instance, root);
                var sessionContext = new SessionContext();
                sessionContext.SetActiveChatId(chatId);
                using var operations = new OperationCoordinator();
                var sender = new RecordingMessageSender();
                var toasts = new RecordingToastHub();
                var archives = new ContinuumArchiveService(truthStore, sessionContext);
                var attemptFinished = 0;
                var completed = 0;
                var workflow = new ContinuumChronicleWorkflow(
                    sender,
                    toasts,
                    truthStore,
                    sessionContext,
                    operations,
                    new ToastLedgerService(truthStore),
                    archives,
                    () => false,
                    _ => attemptFinished++,
                    _ => completed++);

                workflow.Start(chatId, ToastReason.DockClick);

                Assert.True(workflow.IsInFlight);
                Assert.True(operations.IsRunning(GuardedOperationKind.Chronicle));
                var startMessage = Assert.Single(sender.Messages);
                Assert.Equal(WebCommandNames.ContinuumChronicleStart, startMessage.Name);
                var requestId = startMessage.Payload!.Value.GetProperty("requestId").GetString();

                workflow.Complete(new ContinuumCommandMessage
                {
                    ChatId = chatId,
                    RequestId = requestId,
                    CapturedTurns = 8,
                    ElapsedMilliseconds = 250,
                });

                Assert.False(workflow.IsInFlight);
                Assert.False(operations.IsBusy);
                Assert.True(archives.HasChronicleMarker(chatId));
                Assert.Equal(1, attemptFinished);
                Assert.Equal(1, completed);
                Assert.Contains(ToastKey.ChronicleCompleted, toasts.ShownKeys);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public async Task PulseFlushAcknowledgementRunsSupervisedWorkflow()
        {
            var root = Path.Combine(Path.GetTempPath(), "VAL.Tests", Guid.NewGuid().ToString("N"));
            try
            {
                const string chatId = "pulse-workflow";
                var truthStore = new TruthStore(NullTruthTelemetryPublisher.Instance, root);
                truthStore.AppendTruthLine(chatId, 'U', "content for pulse");
                var sessionContext = new SessionContext();
                sessionContext.SetActiveChatId(chatId);
                using var operations = new OperationCoordinator();
                using var backgroundTasks = new BackgroundTaskSupervisor(new FakeLog());
                var sender = new RecordingMessageSender();
                var inbox = new ContinuumInjectInbox();
                var workflow = new ContinuumPulseWorkflow(
                    sender,
                    new RecordingToastHub(),
                    new FakeQuickRefreshService(),
                    inbox,
                    sessionContext,
                    operations,
                    backgroundTasks,
                    new ContinuumArchiveService(truthStore, sessionContext),
                    () => false,
                    action => action());

                workflow.Start(chatId, ToastReason.DockClick);

                Assert.True(workflow.IsInFlight);
                var flushMessage = Assert.Single(sender.Messages);
                Assert.Equal(WebCommandNames.ContinuumCaptureFlush, flushMessage.Name);
                var requestId = flushMessage.Payload!.Value.GetProperty("requestId").GetString();

                workflow.HandleCaptureFlushAck(new ContinuumCommandMessage
                {
                    ChatId = chatId,
                    RequestId = requestId,
                });

                Assert.Equal(1, inbox.Count);
                Assert.Equal("Signal", inbox.Dequeue()!.Mode);

                workflow.CompleteInjection(chatId);
                Assert.False(workflow.IsInFlight);
                Assert.False(operations.IsBusy);
                await backgroundTasks.StopAsync(TimeSpan.FromSeconds(2));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        private sealed class RecordingMessageSender : IWebMessageSender
        {
            public List<MessageEnvelope> Messages { get; } = new();

            public void Send(MessageEnvelope envelope)
            {
                Messages.Add(envelope);
            }
        }

        private sealed class RecordingToastHub : IToastHub
        {
            public bool IsLaunchQuietPeriodActive => false;
            public List<ToastKey> ShownKeys { get; } = new();

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
                ShownKeys.Add(key);
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
                ShownKeys.Add(key);
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

        private sealed class FakeQuickRefreshService : IQuickRefreshService
        {
            public PulseSnapshot BuildPulseSnapshot(string chatId, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return new PulseSnapshot { ChatId = chatId };
            }

            public DeterministicPulseSections BuildDeterministicPulseSections(
                string chatId,
                PulseSnapshot snapshot,
                CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return new DeterministicPulseSections();
            }

            public string BuildDeterministicPulsePacket(
                PulseSnapshot snapshot,
                DeterministicPulseSections deterministicSections,
                CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return "deterministic fallback";
            }

            public EssenceInjectController.InjectSeed CreatePulseSeed(
                string chatId,
                string pulseText,
                string sourceFileName,
                CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return new EssenceInjectController.InjectSeed
                {
                    ChatId = chatId,
                    EssenceText = pulseText,
                    Mode = "Pulse",
                };
            }

            public EssenceInjectController.InjectSeed BuildLegacyPulseSeed(string chatId, CancellationToken token)
            {
                return CreatePulseSeed(chatId, "legacy", "test", token);
            }
        }

        private sealed class FakeLog : ILog
        {
            public void Info(string category, string message) { }
            public void Warn(string category, string message) { }
            public void LogError(string category, string message) { }
            public void Verbose(string category, string message) { }
        }
    }
}
