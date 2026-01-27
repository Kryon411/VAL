namespace VAL.Host.Logging
{
    internal interface ILogSink
    {
        void Write(LogEvent logEvent);
    }
}
