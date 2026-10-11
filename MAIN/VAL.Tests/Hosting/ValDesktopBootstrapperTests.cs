using System;
using System.IO;

using VAL.App.Hosting;
using VAL.Host.Startup;

using Xunit;

namespace VAL.Tests.Hosting
{
    public sealed class ValDesktopBootstrapperTests
    {
        private static readonly string[] ConflictingValidationArgs = { "--smoke", "--validation-session" };
        [Fact]
        public void SmokeRootIsReusableButCannotTargetUserDataOrParentDirectory()
        {
            var root = ValDesktopBootstrapContext.ResolveSmokeDataRoot(null);
            Assert.Equal(root, ValDesktopBootstrapContext.ResolveSmokeDataRoot(root));
            Assert.Throws<ArgumentException>(() => ValDesktopBootstrapContext.ResolveSmokeDataRoot(Path.GetTempPath()));
            Assert.Throws<ArgumentException>(() => ValDesktopBootstrapContext.ResolveSmokeDataRoot(
                Path.Combine(Path.GetTempPath(), "VAL.Smoke", "not-a-guid")));
            Assert.Throws<ArgumentException>(() => ValDesktopBootstrapContext.ResolveSmokeDataRoot(
                Path.Combine(Path.GetTempPath(), "VAL", Guid.NewGuid().ToString("N"))));
        }

        [Fact]
        public void InteractiveValidationRootCannotReuseSmokeOrNormalUserData()
        {
            var root = ValDesktopBootstrapContext.ResolveValidationDataRoot(null);
            Assert.Equal(root, ValDesktopBootstrapContext.ResolveValidationDataRoot(root));
            Assert.Throws<ArgumentException>(() => ValDesktopBootstrapContext.ResolveValidationDataRoot(
                ValDesktopBootstrapContext.ResolveSmokeDataRoot(null)));
            Assert.Throws<ArgumentException>(() => ValDesktopBootstrapContext.ResolveValidationDataRoot(Path.GetTempPath()));
            Assert.Throws<ArgumentException>(() => ValDesktopBootstrapContext.ResolveValidationDataRoot(
                Path.Combine(Path.GetTempPath(), "VAL.Validation", "not-a-guid")));
        }

        [Fact]
        public void ValidationModeCannotBeCombinedWithSmoke()
        {
            Assert.Throws<ArgumentException>(() => ValDesktopBootstrapContext.Create(ConflictingValidationArgs));
        }

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
