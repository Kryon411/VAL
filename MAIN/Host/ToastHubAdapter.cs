using System;

namespace VAL.Host
{
    public sealed class ToastHubAdapter : IToastHub
    {
        public bool IsLaunchQuietPeriodActive => ToastManager.IsLaunchQuietPeriodActive;

        public bool TryShow(
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
            string? ledgerIdOverride = null)
        {
            return ToastHub.TryShow(
                key,
                chatId,
                bypassLaunchQuiet,
                titleOverride,
                subtitleOverride,
                durationOverride,
                groupKeyOverride,
                replaceGroupOverride,
                bypassBurstDedupeOverride,
                oncePerChatOverride,
                ledgerIdOverride);
        }

        public bool TryShowActions(
            ToastKey key,
            (string Label, Action OnClick)[] actions,
            string? chatId = null,
            bool bypassLaunchQuiet = false,
            string? titleOverride = null,
            string? subtitleOverride = null,
            string? groupKeyOverride = null,
            bool? replaceGroupOverride = null,
            bool? oncePerChatOverride = null,
            string? ledgerIdOverride = null)
        {
            return ToastHub.TryShowActions(
                key,
                actions,
                chatId,
                bypassLaunchQuiet,
                titleOverride,
                subtitleOverride,
                groupKeyOverride,
                replaceGroupOverride,
                oncePerChatOverride,
                ledgerIdOverride);
        }

        public void TryShowOperationCancelled(string groupKey)
        {
            ToastHub.TryShowOperationCancelled(groupKey);
        }

        public void DismissGroup(string groupKey)
        {
            ToastManager.DismissGroup(groupKey);
        }
    }
}
