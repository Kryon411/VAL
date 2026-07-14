namespace VAL.App.Host.Services
{
    public interface IDeferredAction
    {
        void Restart();

        void Cancel();
    }
}
