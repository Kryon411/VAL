using VAL.Host;

namespace VAL.Host.Logging
{
    public sealed class ValLogBootstrapper : ILogBootstrapper
    {
        public void Configure(string? logPath, bool enableVerboseLogging)
        {
            ValLog.Configure(logPath, enableVerboseLogging);
        }

        public void AddSink(ILogSink sink)
        {
            ValLog.AddSink(sink);
        }

        public void Info(string category, string message)
        {
            ValLog.Info(category, message);
        }

        public void Warn(string category, string message)
        {
            ValLog.Warn(category, message);
        }

        public void LogError(string category, string message)
        {
            ValLog.Error(category, message);
        }

        public void Verbose(string category, string message)
        {
            ValLog.Verbose(category, message);
        }
    }
}
