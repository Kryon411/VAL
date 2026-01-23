using VAL.Host;

namespace VAL.Host.Services.Adapters
{
    public sealed class OperationCoordinatorAdapter : IOperationCoordinator
    {
        public bool IsBusy => OperationCoordinator.IsBusy;

        public long CurrentOperationId => OperationCoordinator.CurrentOperationId;

        public void RequestCancel()
        {
            OperationCoordinator.RequestCancel();
        }
    }
}
