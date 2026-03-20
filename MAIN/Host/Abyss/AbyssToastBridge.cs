using VAL.Host.Abyss;

namespace VAL.Host
{
    internal static class AbyssToastBridge
    {
        public static void Show(AbyssToastRequest request)
        {
            switch (request.Kind)
            {
                case AbyssToastKind.NoQuery:
                    ToastHub.TryShow(ToastKey.AbyssNoQuery, chatId: request.ChatId);
                    break;
                case AbyssToastKind.Searching:
                    ToastHub.TryShow(ToastKey.AbyssSearching, chatId: request.ChatId);
                    break;
                case AbyssToastKind.NoTruthLogs:
                    ToastHub.TryShow(ToastKey.AbyssNoTruthLogs, chatId: request.ChatId);
                    break;
                case AbyssToastKind.NoMatches:
                    ToastHub.TryShow(ToastKey.AbyssNoMatches, chatId: request.ChatId);
                    break;
                case AbyssToastKind.Matches:
                    ToastHub.TryShow(ToastKey.AbyssMatches, chatId: request.ChatId, titleOverride: request.TitleOverride);
                    break;
                case AbyssToastKind.ResultsWritten:
                    ToastHub.TryShow(ToastKey.AbyssResultsWritten, chatId: request.ChatId);
                    break;
                case AbyssToastKind.Injected:
                    ToastHub.TryShow(ToastKey.AbyssInjected, chatId: request.ChatId, titleOverride: request.TitleOverride);
                    break;
                case AbyssToastKind.NoSelection:
                    ToastHub.TryShow(ToastKey.AbyssNoSelection, chatId: request.ChatId);
                    break;
                case AbyssToastKind.ActionUnavailable:
                    ToastHub.TryShow(
                        ToastKey.ActionUnavailable,
                        chatId: request.ChatId,
                        bypassLaunchQuiet: request.BypassLaunchQuiet);
                    break;
            }
        }
    }
}
