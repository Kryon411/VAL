using VAL.Host.Portal;

namespace VAL.Host.Services
{
    public sealed class PortalRuntimeStateManager : IPortalRuntimeStateManager
    {
        private readonly PortalRuntime _portalRuntime;

        public PortalRuntimeStateManager(PortalRuntime portalRuntime)
        {
            _portalRuntime = portalRuntime ?? throw new System.ArgumentNullException(nameof(portalRuntime));
        }

        public void SetEnabled(bool enabled)
        {
            _portalRuntime.SetEnabled(enabled);
        }

        public void SetPrivacyAllowed(bool allowed)
        {
            _portalRuntime.SetPrivacyAllowed(allowed);
        }

        public void ClearStaging()
        {
            _portalRuntime.ClearStaging();
        }

        public void OpenSnipOverlay()
        {
            _portalRuntime.OpenSnipOverlay();
        }

        public void SendStaged(int max)
        {
            _portalRuntime.SendStaged(max);
        }
    }
}
