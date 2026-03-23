using VAL.Host;

namespace VAL.Host.Logging
{
    public interface ILogBootstrapper : ILog
    {
        void Configure(string? logPath, bool enableVerboseLogging);
        void AddSink(ILogSink sink);
    }
}
