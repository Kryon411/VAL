namespace VAL.Host.Logging
{
    public interface ILogSink
    {
        void Write(LogEvent logEvent);
    }
}
