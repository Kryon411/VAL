namespace VAL.Continuum.Pipeline
{
    public interface IToastLedger
    {
        bool TryMarkShown(string chatId, string toastId);

        bool HasShown(string chatId, string toastId);
    }
}
