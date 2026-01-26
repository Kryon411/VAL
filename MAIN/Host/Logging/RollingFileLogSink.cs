using System;
using System.IO;
using System.Text;

namespace VAL.Host.Logging
{
    internal sealed class RollingFileLogSink : ILogSink
    {
        private readonly object _sync = new();
        private readonly UTF8Encoding _encoding = new(false);
        private readonly long _maxBytes;
        private readonly int _maxFiles;
        private readonly string _directory;
        private readonly string _baseName;
        private readonly string _extension;

        public RollingFileLogSink(string filePath, long maxBytes = 2 * 1024 * 1024, int maxFiles = 5)
        {
            FilePath = filePath ?? string.Empty;
            _maxBytes = Math.Max(1, maxBytes);
            _maxFiles = Math.Max(1, maxFiles);
            _directory = Path.GetDirectoryName(FilePath) ?? ".";
            _baseName = Path.GetFileNameWithoutExtension(FilePath);
            _extension = Path.GetExtension(FilePath);
        }

        public string FilePath { get; }

        public void Write(LogEvent logEvent)
        {
            if (logEvent == null || string.IsNullOrWhiteSpace(FilePath))
                return;

            try
            {
                lock (_sync)
                {
                    Directory.CreateDirectory(_directory);
                    RotateIfNeeded();
                    Append(logEvent.FormattedLine);
                    RotateIfNeeded();
                }
            }
            catch
            {
                // Logging must never throw.
            }
        }

        private void Append(string line)
        {
            using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, _encoding);
            writer.Write(line);
        }

        private void RotateIfNeeded()
        {
            if (_maxFiles <= 1)
            {
                TrimIfOversize();
                return;
            }

            try
            {
                var info = new FileInfo(FilePath);
                if (!info.Exists || info.Length <= _maxBytes)
                    return;

                RotateFiles();
            }
            catch
            {
                // Swallow rotation failures.
            }
        }

        private void TrimIfOversize()
        {
            try
            {
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > _maxBytes)
                    File.Delete(FilePath);
            }
            catch
            {
                // Ignore trim failures.
            }
        }

        private void RotateFiles()
        {
            var maxIndex = _maxFiles - 1;
            var oldest = IndexedPath(maxIndex);

            try
            {
                if (File.Exists(oldest))
                    File.Delete(oldest);
            }
            catch
            {
                // Best-effort cleanup.
            }

            for (var i = maxIndex - 1; i >= 0; i--)
            {
                var source = i == 0 ? FilePath : IndexedPath(i);
                var dest = IndexedPath(i + 1);

                try
                {
                    if (!File.Exists(source))
                        continue;

                    if (File.Exists(dest))
                        File.Delete(dest);

                    File.Move(source, dest);
                }
                catch
                {
                    // Keep rotating what we can.
                }
            }
        }

        private string IndexedPath(int index)
        {
            return Path.Combine(_directory, $"{_baseName}.{index}{_extension}");
        }
    }
}
