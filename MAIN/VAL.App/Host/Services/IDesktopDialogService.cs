namespace VAL.App.Host.Services
{
    public interface IDesktopDialogService
    {
        bool ConfirmWarning(string message, string caption);

        void ShowError(string message, string caption);
    }
}
