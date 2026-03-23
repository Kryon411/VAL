using System;
using System.IO;

namespace VAL.Continuum.Pipeline
{
    /// <summary>
    /// Loads Continuum packet assets from disk.
    /// This is intentionally boring: best-effort IO, never throws.
    /// </summary>
    public static class ContinuumAssetLoader
    {
        public static string LoadSignalPrompt() => LoadSignalPrompt(string.Empty);

        public static string LoadSignalPrompt(string chatId)
        {
            try
            {
                return LoadContinuumText("Signal.Prompt.v1.txt");
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string LoadPulsePacketTemplate() => LoadPulsePacketTemplate(string.Empty);

        public static string LoadPulsePacketTemplate(string chatId)
        {
            try
            {
                return LoadContinuumText("Pulse.Packet.Template.vNext.txt");
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string LoadContinuumText(string fileName)
        {
            var path = FindContinuumModulePath(fileName);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return string.Empty;

            return File.ReadAllText(path);
        }

        private static string? FindContinuumModulePath(string fileName)
        {
            try
            {
                var starts = new[]
                {
                    AppContext.BaseDirectory,
                    Directory.GetCurrentDirectory()
                };

                for (int i = 0; i < starts.Length; i++)
                {
                    var found = FindContinuumModulePathFrom(starts[i], fileName);
                    if (!string.IsNullOrWhiteSpace(found))
                        return found;
                }
            }
            catch { }

            return null;
        }

        private static string? FindContinuumModulePathFrom(string startDir, string fileName)
        {
            try
            {
                var dir = startDir;
                for (int i = 0; i < 10; i++)
                {
                    if (string.IsNullOrWhiteSpace(dir)) break;

                    // <root>\Modules\Continuum\<file>
                    var p1 = Path.Combine(dir, "Modules", "Continuum", fileName);
                    if (File.Exists(p1)) return p1;

                    // <root>\MAIN\Modules\Continuum\<file>
                    var p2 = Path.Combine(dir, "MAIN", "Modules", "Continuum", fileName);
                    if (File.Exists(p2)) return p2;

                    var parent = Directory.GetParent(dir);
                    dir = parent != null ? parent.FullName : string.Empty;
                }
            }
            catch { }

            return null;
        }
    }
}
