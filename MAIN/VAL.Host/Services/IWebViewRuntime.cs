using System;
using System.Threading.Tasks;

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace VAL.Host.Services
{
    public interface IWebViewRuntime
    {
        Task InitializeAsync(WebView2 control);
        bool IsReady { get; }
        CoreWebView2? Core { get; }
        Uri? LastChatUri { get; }
        void PostJson(string json);
        Task ExecuteScriptAsync(string js);
        void Navigate(string url);
        bool TryGoBack();
        event Action<VAL.Host.WebMessaging.WebMessageEnvelope>? WebMessageJsonReceived;
        event Action? NavigationCompleted;
    }
}
