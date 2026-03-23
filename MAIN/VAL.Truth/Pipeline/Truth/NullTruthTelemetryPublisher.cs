namespace VAL.Continuum.Pipeline.Truth
{
    public sealed class NullTruthTelemetryPublisher : ITruthTelemetryPublisher
    {
        public static NullTruthTelemetryPublisher Instance { get; } = new();

        private NullTruthTelemetryPublisher()
        {
        }

        public void PublishTruthBytes(string chatId, long bytes)
        {
        }
    }
}
