using System;
using System.Threading.Tasks;

namespace VAL.Host.Services
{
    public interface IUiThread
    {
        void Invoke(Action action);
        Task InvokeAsync(Action action);
        IDisposable StartTimer(TimeSpan interval, Action tick);
    }
}
