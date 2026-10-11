using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace VAL.Host.Portal;

internal static class PortalImageTransfer
{
    internal static async Task<int> PasteAsync(
        PortalStaging staging,
        int max,
        Func<bool> canPaste,
        Func<bool> canRestore,
        Func<Task<bool>> focusComposer,
        Action<BitmapSource> setClipboardImage,
        Action paste,
        Func<int, Task> delay)
    {
        if (staging.Count == 0 || !canPaste() || !await focusComposer() || !canPaste())
            return 0;

        var items = staging.Drain(Math.Clamp(max, 1, 10));
        var nextItem = 0;
        try
        {
            while (nextItem < items.Length)
            {
                if (!canPaste())
                    break;
                setClipboardImage(items[nextItem]);
                await delay(180);
                if (!canPaste() || !await focusComposer() || !canPaste())
                    break;

                paste();
                // A dispatched paste must not be repeated if a later wait fails.
                nextItem++;
                await delay(710);
            }
            return nextItem;
        }
        finally
        {
            if (canRestore())
                staging.RestoreFront(items.Skip(nextItem));
        }
    }
}
