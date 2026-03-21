namespace VAL.Host
{
    public static class ToastReasonParser
    {
        public static ToastReason Parse(string? reason, ToastReason fallback = ToastReason.Background)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return fallback;

            var normalized = reason.Trim().ToLowerInvariant();

            if (normalized.Contains("dock") || normalized.Contains("click") || normalized.Contains("pointer"))
                return ToastReason.DockClick;

            if (normalized.Contains("hotkey") || normalized.Contains("keydown") || normalized.Contains("keyboard"))
                return ToastReason.Hotkey;

            if (normalized.Contains("attach"))
                return ToastReason.Attach;

            if (normalized.Contains("background"))
                return ToastReason.Background;

            return fallback;
        }
    }
}
