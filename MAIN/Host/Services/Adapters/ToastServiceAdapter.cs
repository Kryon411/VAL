using System.Windows;
using VAL.Host;

namespace VAL.Host.Services.Adapters
{
    public sealed class ToastServiceAdapter : IToastService
    {
        public void Initialize(Window hostWindow)
        {
            ToastManager.Initialize(hostWindow);
        }

        public void ShowMessage(
            string title,
            string? subtitle = null,
            string? groupKey = null,
            bool replaceGroup = false,
            bool bypassBurstDedupe = false)
        {
            ToastManager.ShowCatalog(
                title,
                subtitle,
                ToastManager.ToastDurationBucket.M,
                groupKey,
                replaceGroup,
                bypassBurstDedupe);
        }
    }
}
