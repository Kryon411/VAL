using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using VAL.Host.Services;

namespace VAL.App.Host.Services
{
    public sealed class DesktopToastService : IToastService
    {
        private static readonly TimeSpan LaunchQuietPeriod = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan BurstDedupeWindow = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ActionToastLifetime = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(160);
        private static readonly TimeSpan MessageFadeOutDuration = TimeSpan.FromMilliseconds(600);
        private static readonly TimeSpan ActionFadeOutDuration = TimeSpan.FromMilliseconds(250);

        private readonly IDesktopUiContext _uiContext;
        private readonly DesktopToastPopupHost _popupHost = new();
        private readonly DesktopToastBurstGate _burstGate = new(BurstDedupeWindow);
        private DateTime _initializedUtc = DateTime.MinValue;

        public DesktopToastService(IDesktopUiContext uiContext)
        {
            _uiContext = uiContext ?? throw new ArgumentNullException(nameof(uiContext));
        }

        public bool IsLaunchQuietPeriodActive
        {
            get
            {
                if (_initializedUtc == DateTime.MinValue)
                {
                    return true;
                }

                return (DateTime.UtcNow - _initializedUtc) < LaunchQuietPeriod;
            }
        }

        public void Initialize(Window hostWindow)
        {
            if (_initializedUtc == DateTime.MinValue)
            {
                _initializedUtc = DateTime.UtcNow;
            }

            _popupHost.Initialize(hostWindow);
        }

        public void ShowMessage(
            string title,
            string? subtitle = null,
            ToastDuration duration = ToastDuration.M,
            string? groupKey = null,
            bool replaceGroup = false,
            bool bypassBurstDedupe = false)
        {
            if (!_popupHost.IsInitialized)
            {
                return;
            }

            if (!bypassBurstDedupe && _burstGate.ShouldSuppress(title, subtitle))
            {
                return;
            }

            InvokeOnUi(() =>
            {
                if (!_popupHost.IsInitialized)
                {
                    return;
                }

                var toast = new DesktopToastInstance(
                    DesktopToastVisualFactory.CreateMessageToast(title, subtitle),
                    groupKey);

                ShowToast(
                    toast,
                    replaceGroup,
                    DesktopToastDurationPolicy.Resolve(duration),
                    MessageFadeOutDuration);
            });
        }

        public void ShowActions(
            string title,
            string subtitle,
            (string Label, Action OnClick)[] actions,
            string? groupKey = null,
            bool replaceGroup = false,
            bool sticky = false)
        {
            InvokeOnUi(() =>
            {
                if (!_popupHost.IsInitialized || _burstGate.ShouldSuppress(title, subtitle))
                {
                    return;
                }

                DesktopToastInstance? toast = null;
                var mappedActions = CreateActionBindings(
                    actions,
                    () =>
                    {
                        if (toast != null)
                        {
                            _popupHost.Remove(toast);
                        }
                    });

                toast = new DesktopToastInstance(
                    DesktopToastVisualFactory.CreateActionToast(title, subtitle, mappedActions),
                    groupKey);

                ShowToast(
                    toast,
                    replaceGroup,
                    sticky ? null : ActionToastLifetime,
                    ActionFadeOutDuration);
            });
        }

        public void DismissGroup(string groupKey)
        {
            if (string.IsNullOrWhiteSpace(groupKey))
            {
                return;
            }

            InvokeOnUi(() => _popupHost.RemoveGroup(groupKey));
        }

        private static (string Label, Action OnClick)[] CreateActionBindings(
            (string Label, Action OnClick)[] actions,
            Action dismiss)
        {
            if (actions == null || actions.Length == 0)
            {
                return Array.Empty<(string Label, Action OnClick)>();
            }

            var mappedActions = new (string Label, Action OnClick)[actions.Length];
            for (var i = 0; i < actions.Length; i++)
            {
                var action = actions[i];
                mappedActions[i] = (
                    action.Label ?? string.Empty,
                    () =>
                    {
                        try
                        {
                            action.OnClick?.Invoke();
                        }
                        catch
                        {
                        }

                        dismiss();
                    }
                );
            }

            return mappedActions;
        }

        private void ShowToast(
            DesktopToastInstance toast,
            bool replaceGroup,
            TimeSpan? lifetime,
            TimeSpan fadeOutDuration)
        {
            if (replaceGroup && !string.IsNullOrWhiteSpace(toast.GroupKey))
            {
                _popupHost.RemoveGroup(toast.GroupKey);
            }

            _popupHost.Show(toast);
            BeginFadeIn(toast.View);

            if (lifetime.HasValue)
            {
                ScheduleAutoDismiss(toast, lifetime.Value, fadeOutDuration);
            }
        }

        private static void BeginFadeIn(UIElement toast)
        {
            var fadeIn = new DoubleAnimation(0, 1, FadeInDuration);
            toast.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        private void ScheduleAutoDismiss(
            DesktopToastInstance toast,
            TimeSpan lifetime,
            TimeSpan fadeOutDuration)
        {
            var timer = new DispatcherTimer { Interval = lifetime };
            toast.Timer = timer;
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                toast.Timer = null;
                BeginFadeOut(toast, fadeOutDuration);
            };
            timer.Start();
        }

        private void BeginFadeOut(DesktopToastInstance toast, TimeSpan duration)
        {
            var fadeOut = new DoubleAnimation(1, 0, duration);
            fadeOut.Completed += (_, __) => _popupHost.Remove(toast);
            toast.View.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        private void InvokeOnUi(Action action)
        {
            var dispatcher = _uiContext.Dispatcher;
            if (dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.Invoke(action);
        }
    }
}
