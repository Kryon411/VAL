namespace VAL.Host.Services
{
    public interface IContinuumPump
    {
        void Start();
        Task StopAsync(CancellationToken cancellationToken = default);
    }
}
