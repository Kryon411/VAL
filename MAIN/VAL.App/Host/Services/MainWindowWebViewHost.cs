using System;
using System.Threading.Tasks;

using Microsoft.Web.WebView2.Wpf;

using VAL.Host.Services;

namespace VAL.App.Host.Services
{
    public sealed class MainWindowWebViewHost : IMainWindowWebViewHost
    {
        private readonly WebView2 _control;
        private readonly IWebViewRuntime _webViewRuntime;

        public MainWindowWebViewHost(WebView2 control, IWebViewRuntime webViewRuntime)
        {
            _control = control ?? throw new ArgumentNullException(nameof(control));
            _webViewRuntime = webViewRuntime ?? throw new ArgumentNullException(nameof(webViewRuntime));
        }

        public Task InitializeAsync()
        {
            return _webViewRuntime.InitializeAsync(_control);
        }

        public void ApplyDefaultBackgroundColor(System.Drawing.Color color)
        {
            _control.DefaultBackgroundColor = color;
        }

        public void Navigate(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);
            _control.Source = uri;
        }

        public void Focus()
        {
            _control.Focus();
        }
    }
}
