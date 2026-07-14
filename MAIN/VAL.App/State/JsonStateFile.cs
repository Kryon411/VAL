using System;
using System.IO;
using System.Text.Json;

namespace VAL.App.State
{
    internal enum JsonStateFileReadStatus
    {
        Missing,
        Empty,
        Invalid,
        Success
    }

    internal readonly struct JsonStateFileReadResult<T>
    {
        public JsonStateFileReadResult(JsonStateFileReadStatus status, T? value, Exception? error = null)
        {
            Status = status;
            Value = value;
            Error = error;
        }

        public JsonStateFileReadStatus Status { get; }
        public T? Value { get; }
        public Exception? Error { get; }
        public bool IsSuccess => Status == JsonStateFileReadStatus.Success && Value is not null;
    }

    internal static class JsonStateFile
    {
        public static JsonStateFileReadResult<T> Read<T>(string path, JsonSerializerOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            try
            {
                if (!File.Exists(path))
                    return new JsonStateFileReadResult<T>(JsonStateFileReadStatus.Missing, default);

                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return new JsonStateFileReadResult<T>(JsonStateFileReadStatus.Empty, default);

                var value = JsonSerializer.Deserialize<T>(json, options);
                return value is null
                    ? new JsonStateFileReadResult<T>(JsonStateFileReadStatus.Invalid, default)
                    : new JsonStateFileReadResult<T>(JsonStateFileReadStatus.Success, value);
            }
            catch (Exception ex)
            {
                return new JsonStateFileReadResult<T>(JsonStateFileReadStatus.Invalid, default, ex);
            }
        }

        public static void Write<T>(string path, T value, JsonSerializerOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            var json = JsonSerializer.Serialize(value, options);
            AtomicTextFile.WriteAllText(path, json);
        }
    }
}
