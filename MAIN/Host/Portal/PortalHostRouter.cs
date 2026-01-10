using System;
using System.Text.Json;
using VAL.Host.Commands;

namespace VAL.Host.Portal
{
    internal static class PortalHostRouter
    {
        public static bool Handle(HostCommand cmd)
        {
            var t = cmd.Type ?? string.Empty;

            if (t.Equals("portal.command.set_enabled", StringComparison.OrdinalIgnoreCase))
            {
                bool enabled = false;
                try
                {
                    if (cmd.Root.TryGetProperty("enabled", out var e) &&
                        (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False))
                    {
                        enabled = e.GetBoolean();
                    }
                }
                catch { }

                try { PortalRuntime.SetEnabled(enabled); } catch { }
                return true;
            }

            if (t.Equals("portal.command.open_snip", StringComparison.OrdinalIgnoreCase))
            {
                try { PortalRuntime.OpenSnipOverlay(); } catch { }
                return true;
            }

            if (t.Equals("portal.command.send_staged", StringComparison.OrdinalIgnoreCase))
            {
                int max = 10;
                try
                {
                    if (cmd.Root.TryGetProperty("max", out var m) && m.ValueKind == JsonValueKind.Number)
                        max = Math.Clamp(m.GetInt32(), 1, 10);
                }
                catch { max = 10; }

                try { PortalRuntime.SendStaged(max); } catch { }
                return true;
            }

            return true; // ignore unknown portal.* commands safely
        }
    }
}
