using System;
using System.Threading.Tasks;

namespace VAL.App.Host.Services
{
    public interface IMainWindowWebViewHost
    {
        Task InitializeAsync();

        void ApplyDefaultBackgroundColor(System.Drawing.Color color);

        void Navigate(Uri uri);

        void Focus();
    }
}
