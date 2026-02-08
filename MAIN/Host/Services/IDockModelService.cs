using System;

namespace VAL.Host.Services
{
    public interface IDockModelService
    {
        void Publish(string? chatId = null);

        void UpdatePortalState(bool enabled, bool privacyAllowed, int count);
    }
}
