using System;

namespace VAL.Host.Commands
{
    internal static class PortalCommandHandlers
    {
        public static void HandleSetEnabled(HostCommand cmd)
        {
            try
            {
                if (cmd.TryGetBool("enabled", out var en))
                    VAL.Host.Portal.PortalRuntime.SetEnabled(en);
            }
            catch { }
        }

        public static void HandleOpenSnip(HostCommand cmd)
        {
            try { VAL.Host.Portal.PortalRuntime.OpenSnipOverlay(); } catch { }
        }

        public static void HandleSendStaged(HostCommand cmd)
        {
            try
            {
                int max = 10;
                if (cmd.TryGetInt("max", out var m)) max = m;
                VAL.Host.Portal.PortalRuntime.SendStaged(max);
            }
            catch { }
        }
    }
}
