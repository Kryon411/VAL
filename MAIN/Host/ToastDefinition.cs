using System;
using VAL.Host.Services;

namespace VAL.Host
{
    /// <summary>
    /// Declarative defaults for a toast type.
    /// ToastHub applies gating/policy, then delegates rendering to IToastService.
    /// </summary>
    public sealed record ToastDefinition(
        ToastKey Key,
        string Title,
        string? Subtitle,
        ToastDuration Duration,
        string? GroupKey,
        bool ReplaceGroup,
        bool BypassBurstDedupe,
        bool IsPassive,
        bool OncePerChat,
        string? LedgerId,
        TimeSpan Cooldown
    );
}
