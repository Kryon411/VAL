using System.Windows;

namespace VAL.Host.Services
{
    public interface IToastService
    {
        void Initialize(Window hostWindow);
        void ShowMessage(
            string title,
            string? subtitle = null,
            string? groupKey = null,
            bool replaceGroup = false,
            bool bypassBurstDedupe = false);
    }
}
