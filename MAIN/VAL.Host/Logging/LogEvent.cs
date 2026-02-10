using System;

namespace VAL.Host.Logging
{
    public sealed class LogEvent
    {
        public LogEvent(DateTimeOffset timestamp, LogLevel level, string category, string message, string formattedLine)
        {
            Timestamp = timestamp;
            Level = level;
            Category = category;
            Message = message;
            FormattedLine = formattedLine;
        }

        public DateTimeOffset Timestamp { get; }
        public LogLevel Level { get; }
        public string Category { get; }
        public string Message { get; }
        public string FormattedLine { get; }
    }
}
