namespace VAL.Host.Services
{
    public interface IPortalRuntimeStateManager
    {
        void SetEnabled(bool enabled);
        void SetPrivacyAllowed(bool allowed);
        void ClearStaging();
        void OpenSnipOverlay();
        void SendStaged(int max);
    }
}
