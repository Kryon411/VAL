using System.Threading.Tasks;

namespace VAL.Host.Services
{
    public sealed class SmokeTestState
    {
        public SmokeTestState(SmokeTestSettings settings)
        {
            Settings = settings;
            Completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public SmokeTestSettings Settings { get; }
        public TaskCompletionSource<int> Completion { get; }
    }
}
