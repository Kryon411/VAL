using System.Collections.Generic;

using VAL.Host.Services;

namespace VAL.App.Host.Services
{
    public sealed record DockModelSnapshot
    {
        public string? ChatId { get; init; }

        public bool ContinuumLoggingEnabled { get; init; }

        public bool PortalCaptureEnabled { get; init; }

        public bool PortalEnabled { get; init; }

        public bool PortalPrivacyAllowed { get; init; }

        public int PortalCount { get; init; }

        public IReadOnlyList<ModuleStatusInfo> ModuleStatuses { get; init; } = [];
    }
}
