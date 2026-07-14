using System;
using System.Threading;
using System.Threading.Tasks;

using VAL.Host;
using VAL.Host.Services;

using Xunit;

namespace VAL.Tests.Hosting
{
    public sealed class BackgroundTaskSupervisorTests
    {
        [Fact]
        public async Task RunObservesFailureAndInvokesErrorCallback()
        {
            using var supervisor = new BackgroundTaskSupervisor(new FakeLog());
            var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

            supervisor.Run(
                "test failure",
                _ => throw new InvalidOperationException("boom"),
                onError: exception => failure.TrySetResult(exception));

            var exception = await failure.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.IsType<InvalidOperationException>(exception);
            await supervisor.StopAsync(TimeSpan.FromSeconds(2));
        }

        [Fact]
        public async Task StopAsyncCancelsActiveWorkWithoutReportingFailure()
        {
            using var supervisor = new BackgroundTaskSupervisor(new FakeLog());
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var errorCount = 0;

            supervisor.Run(
                "cancellable test",
                async token =>
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                },
                onError: _ => Interlocked.Increment(ref errorCount));

            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await supervisor.StopAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(0, errorCount);
            Assert.Equal(0, supervisor.ActiveCount);
        }

        [Fact]
        public async Task RunRejectsNewWorkAfterShutdownBegins()
        {
            using var supervisor = new BackgroundTaskSupervisor(new FakeLog());

            await supervisor.StopAsync(TimeSpan.FromSeconds(2));

            Assert.Throws<InvalidOperationException>(() =>
                supervisor.Run("late work", _ => Task.CompletedTask));
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
