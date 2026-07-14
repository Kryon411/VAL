using System.Windows;

namespace VAL.App.Host.Services
{
    public readonly record struct ShellNavigationRefreshPlan(
        bool ShowOverlay,
        bool PulseTopmost,
        bool RequestDockStateSync)
    {
        public static ShellNavigationRefreshPlan None { get; } = new(
            ShowOverlay: false,
            PulseTopmost: false,
            RequestDockStateSync: false);

        public static ShellNavigationRefreshPlan For(WindowState windowState, bool isActive)
        {
            if (windowState == WindowState.Minimized)
            {
                return None;
            }

            return new ShellNavigationRefreshPlan(
                ShowOverlay: true,
                PulseTopmost: isActive,
                RequestDockStateSync: true);
        }
    }
}
