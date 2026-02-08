using System;
using System.IO;
using System.Reflection;

namespace VAL.Host.Services
{
    public sealed class BuildInfo : IBuildInfo
    {
        public string Version { get; }
        public string InformationalVersion { get; }
        public string Environment { get; }
        public string? BuildDate { get; }
        public string? GitSha { get; }

        public BuildInfo()
        {
            var entry = Assembly.GetEntryAssembly();
            var version = entry?.GetName().Version?.ToString() ?? "0.0.0";
            var informational = entry?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            Version = version;
            InformationalVersion = string.IsNullOrWhiteSpace(informational) ? version : informational;
            GitSha = ExtractGitSha(InformationalVersion);
            BuildDate = ResolveBuildDate(entry);

#if DEBUG
            Environment = "Debug";
#else
            Environment = "Release";
#endif
        }

        private static string? ResolveBuildDate(Assembly? entry)
        {
            try
            {
                var location = entry?.Location;
                if (string.IsNullOrWhiteSpace(location) || !File.Exists(location))
                    return null;

                var timestamp = File.GetLastWriteTimeUtc(location);
                if (timestamp == DateTime.MinValue)
                    return null;

                return DateTime.SpecifyKind(timestamp, DateTimeKind.Utc).ToString("u");
            }
            catch
            {
                return null;
            }
        }

        private static string? ExtractGitSha(string? informationalVersion)
        {
            if (string.IsNullOrWhiteSpace(informationalVersion))
                return null;

            var plusIndex = informationalVersion.IndexOf('+');
            if (plusIndex < 0 || plusIndex == informationalVersion.Length - 1)
                return null;

            return informationalVersion[(plusIndex + 1)..];
        }
    }
}
