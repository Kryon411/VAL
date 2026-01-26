using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using VAL.Host.WebMessaging;

namespace VAL.Host.Services
{
    public interface IWebViewRuntime
    {
        Task InitializeAsync(WebView2 control);
        CoreWebView2? Core { get; }
        void PostJson(string json);
        Task ExecuteScriptAsync(string js);
        event Action<WebMessageEnvelope>? WebMessageJsonReceived;
        event Action? NavigationCompleted;
    }
}
