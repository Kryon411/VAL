using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VAL.Host.Services;

namespace VAL.Host.Commands
{
    internal static class NavigationCommandHandlers
    {
        private const string ChatHomeUrl = "https://chatgpt.com/";

        public static void HandleGoChat(HostCommand cmd)
        {
            var services = GetServices();
            if (services == null)
                return;

            var webViewRuntime = services.GetService<IWebViewRuntime>();
            if (webViewRuntime == null)
                return;

            var target = webViewRuntime.LastChatUri?.ToString();
            if (string.IsNullOrWhiteSpace(target))
                target = ChatHomeUrl;

            webViewRuntime.Navigate(target);
        }

        public static void HandleGoBack(HostCommand cmd)
        {
            var services = GetServices();
            if (services == null)
                return;

            var webViewRuntime = services.GetService<IWebViewRuntime>();
            if (webViewRuntime == null)
                return;

            var wentBack = webViewRuntime.TryGoBack();
            if (wentBack)
                return;

            var toastHub = services.GetService<IToastHub>() ?? new ToastHubAdapter();
            var reason = ToastHub.ParseReason(cmd.TryGetString("reason", out var rawReason) ? rawReason : null, ToastReason.DockClick);
            toastHub.TryShow(ToastKey.NavigationNoHistory, origin: ToastOrigin.HostCommand, reason: reason);
        }

        private static IServiceProvider? GetServices()
        {
            return (Application.Current as App)?.Services;
        }
    }
}
