using System;
using System.IO;
using System.Text;

namespace VAL.App.State
{
    internal static class AtomicTextFile
    {
        public static void WriteAllText(string path, string content)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");

            try
            {
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
                {
                    sw.Write(content ?? string.Empty);
                    sw.Flush();
                    fs.Flush(flushToDisk: true);
                }

                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        private static void TryDelete(string? path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }
}
