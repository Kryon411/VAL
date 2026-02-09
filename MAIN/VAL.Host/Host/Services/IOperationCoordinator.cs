namespace VAL.Host.Services
{
    public interface IOperationCoordinator
    {
        bool IsBusy { get; }
        long CurrentOperationId { get; }
        void RequestCancel();
    }
}
