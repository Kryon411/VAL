namespace VAL.Host
{
    public interface ILog
    {
        void Info(string category, string message);
        void Warn(string category, string message);
        void Error(string category, string message);
        void Verbose(string category, string message);
    }
}
