using VAL.Host;

using Xunit;

namespace VAL.Tests.Continuum
{
    public sealed class OperationCoordinatorTests
    {
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
