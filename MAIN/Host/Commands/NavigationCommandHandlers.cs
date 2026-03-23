using System;
using VAL.Contracts;
using VAL.Host.Services;

namespace VAL.Host.Commands
{
    internal sealed class NavigationCommandHandlers : ICommandRegistryContributor
    {
        private const string ChatHomeUrl = "https://chatgpt.com/";
        private readonly IWebViewRuntime _webViewRuntime;
        private readonly IToastHub _toastHub;

        public NavigationCommandHandlers(IWebViewRuntime webViewRuntime, IToastHub toastHub)
        {
            _webViewRuntime = webViewRuntime ?? throw new ArgumentNullException(nameof(webViewRuntime));
            _toastHub = toastHub ?? throw new ArgumentNullException(nameof(toastHub));
        }

        public void Register(CommandRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            registry.Register(new CommandSpec(
                WebCommandNames.NavCommandGoChat,
                "Navigation",
                Array.Empty<string>(),
                HandleGoChat));
            registry.Register(new CommandSpec(
                WebCommandNames.NavCommandGoBack,
                "Navigation",
                Array.Empty<string>(),
                HandleGoBack));
        }

        public void HandleGoChat(HostCommand cmd)
        {
            ArgumentNullException.ThrowIfNull(cmd);

            var target = _webViewRuntime.LastChatUri?.ToString();
            if (string.IsNullOrWhiteSpace(target))
                target = ChatHomeUrl;

            _webViewRuntime.Navigate(target);
        }

        public void HandleGoBack(HostCommand cmd)
        {
            ArgumentNullException.ThrowIfNull(cmd);

            var wentBack = _webViewRuntime.TryGoBack();
            if (wentBack)
                return;

            var reason = ToastReasonParser.Parse(cmd.TryGetString("reason", out var rawReason) ? rawReason : null, ToastReason.DockClick);
            _toastHub.TryShow(ToastKey.NavigationNoHistory, origin: ToastOrigin.HostCommand, reason: reason);
        }
    }
}
