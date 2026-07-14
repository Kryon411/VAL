using System.Windows;

namespace VAL.App.Host.Services
{
    public sealed class MessageBoxDesktopDialogService : IDesktopDialogService
    {
        public bool ConfirmWarning(string message, string caption)
        {
            return MessageBox.Show(
                message,
                caption,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        public void ShowError(string message, string caption)
        {
            MessageBox.Show(
                message,
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
