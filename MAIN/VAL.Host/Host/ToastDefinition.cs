using System;

namespace VAL.Host
{
    /// <summary>
    /// Declarative defaults for a toast type.
    /// ToastHub applies gating/policy, then delegates rendering to ToastManager.
    /// </summary>
    public sealed record ToastDefinition(
        ToastKey Key,
        string Title,
        string? Subtitle,
        ToastManager.ToastDurationBucket Duration,
        string? GroupKey,
        bool ReplaceGroup,
        bool BypassBurstDedupe,
        bool IsPassive,
        bool OncePerChat,
        string? LedgerId,
        TimeSpan Cooldown
    );
}
