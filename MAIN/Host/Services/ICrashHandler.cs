using System.Windows;

namespace VAL.Host.Services
{
    public interface ICrashHandler
    {
        void Register(Application application);
    }
}
