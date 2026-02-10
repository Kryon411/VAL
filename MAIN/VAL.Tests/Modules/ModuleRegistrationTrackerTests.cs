using VAL.Host;
using Xunit;

namespace VAL.Tests.Modules
{
    public sealed class ModuleRegistrationTrackerTests
    {
        [Fact]
        public void TryMarkRegisteredTracksPerCore()
        {
            var tracker = new ModuleRegistrationTracker();
            var coreA = new object();
            var coreB = new object();

            Assert.True(tracker.TryMarkRegistered(coreA, "moduleX"));
            Assert.False(tracker.TryMarkRegistered(coreA, "moduleX"));
            Assert.True(tracker.TryMarkRegistered(coreB, "moduleX"));
            Assert.True(tracker.TryMarkRegistered(coreA, "moduleY"));
        }
    }
}
