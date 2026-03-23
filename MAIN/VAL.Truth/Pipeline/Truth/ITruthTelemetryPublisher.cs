namespace VAL.Continuum.Pipeline.Truth
{
    public interface ITruthTelemetryPublisher
    {
        void PublishTruthBytes(string chatId, long bytes);
    }
}
