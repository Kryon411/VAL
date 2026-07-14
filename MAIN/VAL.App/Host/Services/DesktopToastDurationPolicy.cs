using System;

using VAL.Host.Services;

namespace VAL.App.Host.Services
{
    internal static class DesktopToastDurationPolicy
    {
        public static TimeSpan? Resolve(ToastDuration duration)
        {
            switch (duration)
            {
                case ToastDuration.XS:
                    return TimeSpan.FromSeconds(2);
                case ToastDuration.S:
                    return TimeSpan.FromSeconds(5);
                case ToastDuration.M:
                    return TimeSpan.FromSeconds(10);
                case ToastDuration.L:
                    return TimeSpan.FromSeconds(14);
                case ToastDuration.XL:
                    return TimeSpan.FromSeconds(22);
                case ToastDuration.Sticky:
                    return null;
                default:
                    return TimeSpan.FromSeconds(10);
            }
        }
    }
}
