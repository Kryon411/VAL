using System;
using System.Diagnostics;

namespace VAL.App.Host.Services;

internal sealed record BrowserProcessIdentity(int Id, long StartedUtcTicks)
{
    public static BrowserProcessIdentity? Capture(IWebViewRuntime? runtime)
    {
        try
        {
            var id = runtime?.Core?.BrowserProcessId ?? 0;
            if (id == 0) return null;
            using var process = Process.GetProcessById(checked((int)id));
            return new BrowserProcessIdentity(process.Id, process.StartTime.ToUniversalTime().Ticks);
        }
        catch { return null; }
    }

    public bool WaitForExit(TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(Id);
            // PID reuse must never make an unrelated process part of cleanup.
            if (process.StartTime.ToUniversalTime().Ticks != StartedUtcTicks) return true;
            return process.WaitForExit(checked((int)timeout.TotalMilliseconds));
        }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
        catch { return false; }
    }
}
