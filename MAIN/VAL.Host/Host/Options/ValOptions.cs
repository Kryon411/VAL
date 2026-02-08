using System;
using System.IO;

namespace VAL.Host.Options
{
    public sealed class ValOptions
    {
        public const string SectionName = "Val";

        public static string DefaultDataRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VAL");

        public const string DefaultLogsPath = "Logs";
        public const string DefaultProfilePath = "Profile";

        public string DataRoot { get; set; } = DefaultDataRoot;
        public string LogsPath { get; set; } = DefaultLogsPath;
        public string ModulesPath { get; set; } = string.Empty;
        public string ProfilePath { get; set; } = DefaultProfilePath;
        public bool EnableVerboseLogging { get; set; }

        public void ApplyDefaults()
        {
            if (string.IsNullOrWhiteSpace(DataRoot))
                DataRoot = DefaultDataRoot;

            if (string.IsNullOrWhiteSpace(LogsPath))
                LogsPath = DefaultLogsPath;

            if (string.IsNullOrWhiteSpace(ProfilePath))
                ProfilePath = DefaultProfilePath;

            ModulesPath ??= string.Empty;
        }
    }
}
