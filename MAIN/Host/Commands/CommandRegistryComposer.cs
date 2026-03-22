using System;
using VAL.Host.Commands;

namespace VAL.Host
{
    internal sealed class CommandRegistryComposer
    {
        private readonly VoidCommandHandlers _voidCommandHandlers;
        private readonly PortalCommandHandlers _portalCommandHandlers;
        private readonly PrivacyCommandHandlers _privacyCommandHandlers;
        private readonly ToolsCommandHandlers _toolsCommandHandlers;
        private readonly NavigationCommandHandlers _navigationCommandHandlers;
        private readonly DockCommandHandlers _dockCommandHandlers;
        private readonly AbyssCommandHandlers _abyssCommandHandlers;
        private readonly ContinuumCommandHandlers _continuumCommandHandlers;

        public CommandRegistryComposer(
            ContinuumCommandHandlers continuumCommandHandlers,
            VoidCommandHandlers voidCommandHandlers,
            PortalCommandHandlers portalCommandHandlers,
            PrivacyCommandHandlers privacyCommandHandlers,
            ToolsCommandHandlers toolsCommandHandlers,
            NavigationCommandHandlers navigationCommandHandlers,
            DockCommandHandlers dockCommandHandlers,
            AbyssCommandHandlers abyssCommandHandlers)
        {
            _continuumCommandHandlers = continuumCommandHandlers ?? throw new ArgumentNullException(nameof(continuumCommandHandlers));
            _voidCommandHandlers = voidCommandHandlers ?? throw new ArgumentNullException(nameof(voidCommandHandlers));
            _portalCommandHandlers = portalCommandHandlers ?? throw new ArgumentNullException(nameof(portalCommandHandlers));
            _privacyCommandHandlers = privacyCommandHandlers ?? throw new ArgumentNullException(nameof(privacyCommandHandlers));
            _toolsCommandHandlers = toolsCommandHandlers ?? throw new ArgumentNullException(nameof(toolsCommandHandlers));
            _navigationCommandHandlers = navigationCommandHandlers ?? throw new ArgumentNullException(nameof(navigationCommandHandlers));
            _dockCommandHandlers = dockCommandHandlers ?? throw new ArgumentNullException(nameof(dockCommandHandlers));
            _abyssCommandHandlers = abyssCommandHandlers ?? throw new ArgumentNullException(nameof(abyssCommandHandlers));
        }

        public void Register(CommandRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            CommandRegistryFactory.RegisterCommands(
                registry,
                _continuumCommandHandlers.HandleContinuumCommand,
                _voidCommandHandlers.HandleSetEnabled,
                _portalCommandHandlers.HandleSetEnabled,
                _portalCommandHandlers.HandleOpenSnip,
                _portalCommandHandlers.HandleSendStaged,
                _privacyCommandHandlers.HandleSetContinuumLogging,
                _privacyCommandHandlers.HandleSetPortalCapture,
                _privacyCommandHandlers.HandleOpenDataFolder,
                _privacyCommandHandlers.HandleWipeData,
                _toolsCommandHandlers.HandleOpenTruthHealth,
                _toolsCommandHandlers.HandleOpenDiagnostics,
                _navigationCommandHandlers.HandleGoChat,
                _navigationCommandHandlers.HandleGoBack,
                _dockCommandHandlers.HandleRequestModel,
                _dockCommandHandlers.HandleUiStateGet,
                _dockCommandHandlers.HandleUiStateSet,
                _abyssCommandHandlers.HandleOpenQueryUi,
                _abyssCommandHandlers.HandleSearch,
                _abyssCommandHandlers.HandleRetryLast,
                _abyssCommandHandlers.HandleInjectResult,
                _abyssCommandHandlers.HandleInjectResults,
                _abyssCommandHandlers.HandleLast,
                _abyssCommandHandlers.HandleOpenSource,
                _abyssCommandHandlers.HandleClearResults,
                _abyssCommandHandlers.HandleDisregard,
                _abyssCommandHandlers.HandleGetResults,
                _abyssCommandHandlers.HandleInjectPrompt,
                _abyssCommandHandlers.HandleInject);
        }
    }
}
