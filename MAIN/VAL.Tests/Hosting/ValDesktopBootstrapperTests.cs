using System;

using VAL.App.Hosting;
using VAL.Host.Startup;

using Xunit;

namespace VAL.Tests.Hosting
{
    public sealed class ValDesktopBootstrapperTests
    {
        [Fact]
        public void ApplyCrashGuardSafeModeEnablesSafeModeWhenNotExplicit()
        {
            var startupOptions = new StartupOptions(false, false, Array.Empty<string>());

            ValDesktopBootstrapper.ApplyCrashGuardSafeMode(startupOptions, crashGuardSafeMode: true);

            Assert.True(startupOptions.SafeMode);
        }

        [Fact]
        public void ApplyCrashGuardSafeModeDoesNotOverrideExplicitChoice()
        {
            var startupOptions = new StartupOptions(false, true, Array.Empty<string>());

            ValDesktopBootstrapper.ApplyCrashGuardSafeMode(startupOptions, crashGuardSafeMode: true);

            Assert.False(startupOptions.SafeMode);
        }
    }
}
