using System;
using System.Windows;

namespace VAL.Host.Services
{
    public interface IToastService
    {
        bool IsLaunchQuietPeriodActive { get; }

        void Initialize(Window hostWindow);

        void ShowMessage(
            string title,
            string? subtitle = null,
            ToastDuration duration = ToastDuration.M,
            string? groupKey = null,
            bool replaceGroup = false,
            bool bypassBurstDedupe = false);

        void ShowActions(
            string title,
            string subtitle,
            (string Label, Action OnClick)[] actions,
            string? groupKey = null,
            bool replaceGroup = false,
            bool sticky = false);

        void DismissGroup(string groupKey);
    }
}
