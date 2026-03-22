using System;
using System.Windows;
using VAL.Host;

namespace VAL.Host.Services.Adapters
{
    public sealed class ToastServiceAdapter : IToastService
    {
        public bool IsLaunchQuietPeriodActive => ToastManager.IsLaunchQuietPeriodActive;

        public void Initialize(Window hostWindow)
        {
            ToastManager.Initialize(hostWindow);
        }

        public void ShowMessage(
            string title,
            string? subtitle = null,
            ToastDuration duration = ToastDuration.M,
            string? groupKey = null,
            bool replaceGroup = false,
            bool bypassBurstDedupe = false)
        {
            ToastManager.ShowCatalog(
                title,
                subtitle,
                MapDuration(duration),
                groupKey,
                replaceGroup,
                bypassBurstDedupe);
        }

        public void ShowActions(
            string title,
            string subtitle,
            (string Label, Action OnClick)[] actions,
            string? groupKey = null,
            bool replaceGroup = false,
            bool sticky = false)
        {
            ToastManager.ShowActions(
                title,
                subtitle,
                groupKey,
                replaceGroup,
                sticky,
                actions);
        }

        public void DismissGroup(string groupKey)
        {
            ToastManager.DismissGroup(groupKey);
        }

        private static ToastManager.ToastDurationBucket MapDuration(ToastDuration duration)
        {
            return duration switch
            {
                ToastDuration.XS => ToastManager.ToastDurationBucket.XS,
                ToastDuration.S => ToastManager.ToastDurationBucket.S,
                ToastDuration.M => ToastManager.ToastDurationBucket.M,
                ToastDuration.L => ToastManager.ToastDurationBucket.L,
                ToastDuration.XL => ToastManager.ToastDurationBucket.XL,
                ToastDuration.Sticky => ToastManager.ToastDurationBucket.Sticky,
                _ => throw new ArgumentOutOfRangeException(nameof(duration), duration, null),
            };
        }
    }
}
