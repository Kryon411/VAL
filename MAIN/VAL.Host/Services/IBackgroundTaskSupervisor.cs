using System;
using System.Threading;
using System.Threading.Tasks;

namespace VAL.Host.Services
{
    public interface IBackgroundTaskSupervisor
    {
        int ActiveCount { get; }

        void Run(
            string operation,
            Func<CancellationToken, Task> work,
            Action<Exception>? onError = null,
            CancellationToken cancellationToken = default);

        Task StopAsync(TimeSpan timeout);
    }
}
