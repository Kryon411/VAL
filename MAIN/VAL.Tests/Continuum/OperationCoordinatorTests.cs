using VAL.Host;

using Xunit;

namespace VAL.Tests.Continuum
{
    public sealed class OperationCoordinatorTests
    {
        [Fact]
        public void DeadlineForPreviousOperationCannotCancelItsReplacement()
        {
            using var coordinator = new OperationCoordinator();
            Assert.True(coordinator.TryBegin(GuardedOperationKind.Pulse, out _));
            var old = coordinator.CurrentOperationId;
            coordinator.End(GuardedOperationKind.Pulse);
            Assert.True(coordinator.TryBegin(GuardedOperationKind.Chronicle, out var current));
            Assert.False(coordinator.RequestCancel(GuardedOperationKind.Pulse, old));
            Assert.False(current.IsCancellationRequested);
            Assert.True(coordinator.RequestCancel(GuardedOperationKind.Chronicle, coordinator.CurrentOperationId));
            Assert.True(current.IsCancellationRequested);
        }

        [Fact]
        public void ForceEndDoesNotReuseOperationIdentity()
        {
            using var coordinator = new OperationCoordinator();
            Assert.True(coordinator.TryBegin(GuardedOperationKind.Pulse, out _));
            var first = coordinator.CurrentOperationId;
            coordinator.ForceEndAll();
            Assert.True(coordinator.TryBegin(GuardedOperationKind.Pulse, out _));
            Assert.True(coordinator.CurrentOperationId > first);
        }

        [Fact]
        public void EndCancelsOutstandingOperationToken()
        {
            using var coordinator = new OperationCoordinator();
            Assert.True(coordinator.TryBegin(GuardedOperationKind.Pulse, out var token));

            coordinator.End(GuardedOperationKind.Pulse);

            Assert.True(token.IsCancellationRequested);
            Assert.False(coordinator.IsBusy);
        }
    }
}
