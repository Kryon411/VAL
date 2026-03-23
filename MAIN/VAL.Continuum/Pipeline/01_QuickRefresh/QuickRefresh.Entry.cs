using System;
using System.Threading;
using VAL.Continuum.Pipeline.Inject;

namespace VAL.Continuum.Pipeline.QuickRefresh
{
    /// <summary>
    /// Pulse Entry (single spine):
    /// Truth -> Essence-M -> Queue seed for sealed injector (no autosend).
    /// </summary>
    public static class QuickRefreshEntry
    {
        public static void Run(string chatId, IContinuumInjectInbox injectInbox)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                throw new ArgumentNullException(nameof(chatId));

            QuickRefreshFlow.Run(chatId, injectInbox, CancellationToken.None);
        }

        public static void Run(string chatId, IContinuumInjectInbox injectInbox, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                throw new ArgumentNullException(nameof(chatId));

            QuickRefreshFlow.Run(chatId, injectInbox, token);
        }
    }
}
