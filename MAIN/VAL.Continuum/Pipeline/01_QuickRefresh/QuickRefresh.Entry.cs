using System;
using System.Threading;

namespace VAL.Continuum.Pipeline.QuickRefresh
{
    /// <summary>
    /// Pulse Entry (single spine):
    /// Truth -> Essence-M -> Queue seed for sealed injector (no autosend).
    /// </summary>
    public static class QuickRefreshEntry
    {
        public static void Run(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                throw new ArgumentNullException(nameof(chatId));

            QuickRefreshFlow.Run(chatId, CancellationToken.None);
        }

        public static void Run(string chatId, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                throw new ArgumentNullException(nameof(chatId));

            QuickRefreshFlow.Run(chatId, token);
        }
    }
}
