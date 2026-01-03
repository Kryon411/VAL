using System;
using System.IO;
using System.Text;

namespace VAL.Continuum.Pipeline
{
    /// <summary>
    /// Minimal atomic file helpers for operational safety.
    /// Writes are committed via a temp file and an atomic replace/move.
    /// </summary>
    internal static class AtomicFile
    {
        public static void WriteAllTextAtomic(string path, string content)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var tmp = GetTempPath(path);

            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                using (var sw = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                {
                    sw.Write(content ?? string.Empty);
                    sw.Flush();
                    fs.Flush(flushToDisk: true);
                }

                ReplaceAtomic(tmp, path);
            }
            finally
            {
                TryDelete(tmp);
            }
        }

        public static void AppendAllTextAtomic(string path, string appendText)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            // If the file doesn't exist, a single atomic write is enough.
            if (!File.Exists(path))
            {
                WriteAllTextAtomic(path, appendText ?? string.Empty);
                return;
            }

            var tmp = GetTempPath(path);

            try
            {
                // Stream-copy existing content into tmp, then append.
                using (var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    src.CopyTo(dst);

                    using (var sw = new StreamWriter(dst, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true))
                    {
                        sw.Write(appendText ?? string.Empty);
                        sw.Flush();
                    }

                    dst.Flush(flushToDisk: true);
                }

                ReplaceAtomic(tmp, path);
            }
            finally
            {
                TryDelete(tmp);
            }
        }

        public static void ReplaceAtomic(string tempPath, string finalPath)
        {
            if (string.IsNullOrWhiteSpace(tempPath))
                throw new ArgumentNullException(nameof(tempPath));
            if (string.IsNullOrWhiteSpace(finalPath))
                throw new ArgumentNullException(nameof(finalPath));

            var dir = Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(finalPath))
            {
                // File.Replace is atomic on Windows when source/target are on the same volume.
                File.Replace(tempPath, finalPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, finalPath);
            }
        }

        private static string GetTempPath(string finalPath)
        {
            // Keep temp files beside the final file so replace is atomic.
            return finalPath + ".tmp." + Guid.NewGuid().ToString("N");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // best-effort
            }
        }
    }
}
