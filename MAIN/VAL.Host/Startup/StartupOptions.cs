using System;

namespace VAL.Host.Startup
{
    public sealed class StartupOptions
    {
        public StartupOptions(bool safeMode, bool safeModeExplicit, string[] rawArgs)
        {
            SafeMode = safeMode;
            SafeModeExplicit = safeModeExplicit;
            RawArgs = rawArgs ?? Array.Empty<string>();
        }

        public bool SafeMode { get; set; }

        public bool SafeModeExplicit { get; }

        public string[] RawArgs { get; }
    }
}
