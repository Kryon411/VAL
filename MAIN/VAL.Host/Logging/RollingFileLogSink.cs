using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using VAL.Host;

namespace VAL.Host.Logging
{
    public sealed class RollingFileLogSink : ILogSink
    {
        private readonly object _sync = new();
        private readonly UTF8Encoding _encoding = new(false);
        private readonly long _maxBytes;
        private readonly int _maxFiles;
        private readonly string _directory;
        private readonly string _baseName;
        private readonly string _extension;
        private static readonly RateLimiter RateLimiter = new();
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);
        [ThreadStatic] private static bool _loggingWarning;

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
                    RotateIfNeeded(logEvent.FormattedLine);
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

        private void RotateIfNeeded(string? pendingLine = null)
        {
            if (_maxFiles <= 1)
            {
                TrimIfOversize(pendingLine);
                return;
            }

            try
            {
                var info = new FileInfo(FilePath);
                var projected = info.Exists ? info.Length : 0;
                if (!string.IsNullOrEmpty(pendingLine))
                    projected += _encoding.GetByteCount(pendingLine);

                if (projected <= _maxBytes)
                    return;

                RotateFiles();
            }
            catch (Exception ex)
            {
                LogRotationFailure("rotate_check", ex);
            }
        }

        private void TrimIfOversize(string? pendingLine = null)
        {
            try
            {
                var info = new FileInfo(FilePath);
                var projected = info.Exists ? info.Length : 0;
                if (!string.IsNullOrEmpty(pendingLine))
                    projected += _encoding.GetByteCount(pendingLine);

                if (projected > _maxBytes)
                    File.Delete(FilePath);
            }
            catch (Exception ex)
            {
                LogRotationFailure("trim", ex);
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
            catch (Exception ex)
            {
                LogRotationFailure("delete_oldest", ex);
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
                catch (Exception ex)
                {
                    LogRotationFailure("move", ex);
                }
            }
        }

        private string IndexedPath(int index)
        {
            return Path.Combine(_directory, $"{_baseName}.{index}{_extension}");
        }

        private static void LogRotationFailure(string action, Exception ex)
        {
            if (_loggingWarning)
                return;

            if (!RateLimiter.Allow($"log.rotate.{action}", LogInterval))
                return;

            try
            {
                _loggingWarning = true;
                ValLog.Warn(nameof(RollingFileLogSink),
                    $"Rolling log rotation failed ({action}). {ex.GetType().Name}: {LogSanitizer.Sanitize(ex.Message)}");
            }
            catch
            {
                Trace.WriteLine($"[VAL] Rolling log rotation failed ({action}). {ex.GetType().Name}");
            }
            finally
            {
                _loggingWarning = false;
            }
        }
    }
}
