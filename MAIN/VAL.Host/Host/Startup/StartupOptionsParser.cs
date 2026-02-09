using System;

namespace VAL.Host.Startup
{
    public static class StartupOptionsParser
    {
        public static StartupOptions Parse(string[]? args)
        {
            try
            {
                var raw = args ?? Array.Empty<string>();
                var safeMode = false;

                foreach (var arg in raw)
                {
                    if (string.IsNullOrWhiteSpace(arg))
                        continue;

                    if (IsSafeModeArg(arg))
                    {
                        safeMode = true;
                        break;
                    }
                }

                return new StartupOptions(safeMode, safeMode, raw);
            }
            catch
            {
                return new StartupOptions(false, false, Array.Empty<string>());
            }
        }

        private static bool IsSafeModeArg(string arg)
        {
            return arg.Equals("--safe", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--nomodules", StringComparison.OrdinalIgnoreCase);
        }
    }
}
