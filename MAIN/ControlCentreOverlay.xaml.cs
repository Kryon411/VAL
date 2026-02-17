using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace VAL
{
    public partial class ControlCentreOverlay : Window
    {
        public event EventHandler? ToggleRequested;

        public ControlCentreOverlay()
        {
            InitializeComponent();
            Loaded += ControlCentreOverlay_Loaded;
        }

        private void ControlCentreOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeIcon();
        }

        private void LauncherButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleRequested?.Invoke(this, EventArgs.Empty);
        }

        private void InitializeIcon()
        {
            var imageSource = TryLoadIcon(Path.Combine(AppContext.BaseDirectory, "Icons", "VAL_Blue_Lens.ico"));

            LauncherButton.ApplyTemplate();
            var buttonImage = LauncherButton.Template.FindName("LauncherImage", LauncherButton) as Image;
            var fallbackText = LauncherButton.Template.FindName("LauncherFallbackText", LauncherButton) as TextBlock;

            if (imageSource != null)
            {
                if (buttonImage != null)
                {
                    buttonImage.Source = imageSource;
                    buttonImage.Visibility = Visibility.Visible;
                }

                if (fallbackText != null)
                {
                    fallbackText.Visibility = Visibility.Collapsed;
                }

                return;
            }

            if (buttonImage != null)
            {
                buttonImage.Visibility = Visibility.Collapsed;
            }

            if (fallbackText != null)
            {
                fallbackText.Visibility = Visibility.Visible;
            }
        }

        private static BitmapFrame? TryLoadIcon(string iconPath)
        {
            try
            {
                if (!File.Exists(iconPath))
                {
                    return null;
                }

                var uri = new Uri(iconPath, UriKind.Absolute);
                var bitmap = BitmapFrame.Create(uri, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}
