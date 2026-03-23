using VAL.Continuum.Pipeline.Inject;
using Xunit;

namespace VAL.Tests.Continuum
{
    public sealed class ContinuumInjectInboxTests
    {
        [Fact]
        public void InstancesMaintainIndependentQueueState()
        {
            var first = new ContinuumInjectInbox();
            var second = new ContinuumInjectInbox();
            var seed = new EssenceInjectController.InjectSeed
            {
                ChatId = "chat-1",
                EssenceText = "payload"
            };

            first.Enqueue(seed);

            Assert.Equal(1, first.Count);
            Assert.Equal(0, second.Count);
            Assert.True(first.TryDequeue(out var dequeued));
            Assert.Same(seed, dequeued);
            Assert.Equal(0, first.Count);
            Assert.False(second.TryDequeue(out _));
        }

        [Fact]
        public void EnqueueIgnoresEmptySeeds()
        {
            var inbox = new ContinuumInjectInbox();

            inbox.Enqueue(new EssenceInjectController.InjectSeed());

            Assert.Equal(0, inbox.Count);
            Assert.False(inbox.TryDequeue(out _));
        }
    }
}
