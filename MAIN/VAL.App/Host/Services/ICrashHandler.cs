using System.Windows;

namespace VAL.App.Host.Services
{
    public interface ICrashHandler
    {
        void Register(Application application);
    }
}
