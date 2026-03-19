using VAL.Host.Portal;

namespace VAL.Host.Services
{
    public sealed class PortalRuntimeStateManager : IPortalRuntimeStateManager
    {
        public void SetEnabled(bool enabled)
        {
            PortalRuntime.SetEnabled(enabled);
        }

        public void SetPrivacyAllowed(bool allowed)
        {
            PortalRuntime.SetPrivacyAllowed(allowed);
        }

        public void ClearStaging()
        {
            PortalRuntime.ClearStaging();
        }

        public void OpenSnipOverlay()
        {
            PortalRuntime.OpenSnipOverlay();
        }

        public void SendStaged(int max)
        {
            PortalRuntime.SendStaged(max);
        }
    }
}
