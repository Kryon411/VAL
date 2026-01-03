using System;
using System.Windows;

namespace VAL
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            try
            {
                var app = new Application();
                var mainWindow = new MainWindow();
                app.Run(mainWindow);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Application startup failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
