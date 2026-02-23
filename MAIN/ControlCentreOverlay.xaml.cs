using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VAL
{
    public partial class ControlCentreOverlay : Window
    {
        private const int WmNchittest = 0x0084;
        private const int HtClient = 1;
        private const int HtTransparent = -1;
        private const double MinOverlaySize = 24;
        private const double MaxOverlaySize = 120;

        private HwndSource? _hwndSource;
        private Point _dragStartScreen;
        private Rect _dragStartBounds;
        private bool _isDragging;

        public event EventHandler? LauncherClicked;
        public event EventHandler<Rect>? GeometryCommitted;

        public bool IsLayoutMode { get; private set; }

        public ControlCentreOverlay()
        {
            InitializeComponent();
            Loaded += ControlCentreOverlay_Loaded;
            SourceInitialized += ControlCentreOverlay_SourceInitialized;
            Closed += ControlCentreOverlay_Closed;
        }

        public void SetLayoutMode(bool enabled)
        {
            IsLayoutMode = enabled;
            ResizeThumb.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            LayoutOutline.BorderBrush = enabled
                ? new SolidColorBrush(Color.FromArgb(170, 125, 205, 255))
                : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            InvalidateVisual();
        }

        public void ApplyGeometry(double x, double y, double width, double height)
        {
            Left = x;
            Top = y;
            Width = Math.Clamp(width, MinOverlaySize, MaxOverlaySize);
            Height = Math.Clamp(height, MinOverlaySize, MaxOverlaySize);
            LauncherButton.Width = Width;
            LauncherButton.Height = Height;
            LayoutRoot.Width = Width;
            LayoutRoot.Height = Height;
        }

        private void ControlCentreOverlay_SourceInitialized(object? sender, EventArgs e)
        {
            if (PresentationSource.FromVisual(this) is HwndSource source)
            {
                _hwndSource = source;
                _hwndSource.AddHook(WndProc);
            }
        }

        private void ControlCentreOverlay_Closed(object? sender, EventArgs e)
        {
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WmNchittest)
            {
                return IntPtr.Zero;
            }

            var screen = GetPointFromLParam(lParam);
            var hit = HitTestInteractive(screen);
            handled = true;
            return new IntPtr(hit ? HtClient : HtTransparent);
        }

        private bool HitTestInteractive(Point screenPoint)
        {
            if (IsPointOverElement(screenPoint, LauncherButton))
            {
                return true;
            }

            if (!IsLayoutMode)
            {
                return false;
            }

            return IsPointOverElement(screenPoint, ResizeThumb);
        }

        private bool IsPointOverElement(Point screenPoint, FrameworkElement? element)
        {
            if (element == null || !element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            {
                return false;
            }

            var topLeft = element.PointToScreen(new Point(0, 0));
            var dpi = VisualTreeHelper.GetDpi(this);
            var rect = new Rect(
                topLeft.X / dpi.DpiScaleX,
                topLeft.Y / dpi.DpiScaleY,
                element.ActualWidth,
                element.ActualHeight);
            return rect.Contains(screenPoint);
        }

        private static Point GetPointFromLParam(IntPtr lParam)
        {
            var x = (short)(lParam.ToInt32() & 0xFFFF);
            var y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
            return new Point(x, y);
        }

        private void ControlCentreOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeIcon();
            ApplyGeometry(Left, Top, Width, Height);
        }

        private void LauncherButton_Click(object sender, RoutedEventArgs e)
        {
            LauncherClicked?.Invoke(this, EventArgs.Empty);
        }

        private void LauncherButton_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsLayoutMode)
            {
                return;
            }

            _dragStartScreen = PointToScreen(e.GetPosition(this));
            _dragStartBounds = new Rect(Left, Top, Width, Height);
            _isDragging = true;
            Mouse.Capture(LauncherButton);
            e.Handled = true;
        }

        private void LauncherButton_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || e.RightButton != MouseButtonState.Pressed)
            {
                return;
            }

            var current = PointToScreen(e.GetPosition(this));
            var dx = current.X - _dragStartScreen.X;
            var dy = current.Y - _dragStartScreen.Y;
            Left = _dragStartBounds.Left + dx;
            Top = _dragStartBounds.Top + dy;
        }

        private void LauncherButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging)
            {
                return;
            }

            _isDragging = false;
            Mouse.Capture(null);
            GeometryCommitted?.Invoke(this, new Rect(Left, Top, Width, Height));
            e.Handled = true;
        }

        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!IsLayoutMode)
            {
                return;
            }

            var nextWidth = Math.Clamp(Width + e.HorizontalChange, MinOverlaySize, MaxOverlaySize);
            var nextHeight = Math.Clamp(Height + e.VerticalChange, MinOverlaySize, MaxOverlaySize);
            ApplyGeometry(Left, Top, nextWidth, nextHeight);
        }

        private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            GeometryCommitted?.Invoke(this, new Rect(Left, Top, Width, Height));
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
