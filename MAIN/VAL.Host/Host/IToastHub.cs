using System;

namespace VAL.Host
{
    public interface IToastHub
    {
        bool IsLaunchQuietPeriodActive { get; }

        bool TryShow(
            ToastKey key,
            string? chatId = null,
            bool bypassLaunchQuiet = false,
            string? titleOverride = null,
            string? subtitleOverride = null,
            ToastManager.ToastDurationBucket? durationOverride = null,
            string? groupKeyOverride = null,
            bool? replaceGroupOverride = null,
            bool? bypassBurstDedupeOverride = null,
            bool? oncePerChatOverride = null,
            string? ledgerIdOverride = null,
            ToastOrigin origin = ToastOrigin.Unknown,
            ToastReason reason = ToastReason.Unknown);

        bool TryShowActions(
            ToastKey key,
            (string Label, Action OnClick)[] actions,
            string? chatId = null,
            bool bypassLaunchQuiet = false,
            string? titleOverride = null,
            string? subtitleOverride = null,
            string? groupKeyOverride = null,
            bool? replaceGroupOverride = null,
            bool? oncePerChatOverride = null,
            string? ledgerIdOverride = null,
            ToastOrigin origin = ToastOrigin.Unknown,
            ToastReason reason = ToastReason.Unknown);

        void TryShowOperationCancelled(
            string groupKey,
            ToastOrigin origin = ToastOrigin.Unknown,
            ToastReason reason = ToastReason.Unknown);

        void DismissGroup(string groupKey);
    }
}
