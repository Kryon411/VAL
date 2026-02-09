using System;

namespace VAL.Host.Options
{
    public sealed class ModuleOptions
    {
        public const string SectionName = "Modules";

        public string[] EnabledModules { get; set; } = Array.Empty<string>();

        public void ApplyDefaults()
        {
            EnabledModules ??= Array.Empty<string>();
        }
    }
}
