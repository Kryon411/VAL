using System.Windows;
using VAL.Host;

namespace VAL.Host.Services.Adapters
{
    public sealed class ToastServiceAdapter : IToastService
    {
        public void Initialize(Window hostWindow)
        {
            ToastHub.Initialize(hostWindow);
        }
    }
}
