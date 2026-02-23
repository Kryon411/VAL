using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VAL
{
    public partial class ControlCentreOverlay : Window
    {
        private const int WmNcHitTest = 0x0084;
        private const int HtTransparent = -1;
        private const int HtClient = 1;
        private const double MinOverlaySize = 26;
        private const double MaxOverlaySize = 96;

        private bool _isDragging;
        private Point _dragOffset;
        private bool _isResizing;
        private Point _resizeStart;
        private double _startWidth;
        private double _startHeight;
        private HwndSource? _hwndSource;

        public event EventHandler? Clicked;
        public event EventHandler? GeometryChanged;

        public bool LayoutModeEnabled
        {
            get => ResizeHandle.Visibility == Visibility.Visible;
            set
            {
                ResizeHandle.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                LauncherButton.Cursor = value ? Cursors.SizeAll : Cursors.Hand;
            }
        }

        public ControlCentreOverlay()
        {
            InitializeComponent();
            Loaded += ControlCentreOverlay_Loaded;
            SourceInitialized += ControlCentreOverlay_SourceInitialized;
            Closed += ControlCentreOverlay_Closed;
        }

        private void ControlCentreOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeIcon();
        }

        private void ControlCentreOverlay_SourceInitialized(object? sender, EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            _hwndSource = HwndSource.FromHwnd(handle);
            _hwndSource?.AddHook(WndProc);
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
            if (msg != WmNcHitTest)
            {
                return IntPtr.Zero;
            }

            var point = GetPointFromLParam(lParam);
            var screenPoint = PointFromScreen(new Point(point.X, point.Y));
            var buttonBounds = LauncherButton.TransformToAncestor(RootGrid)
                .TransformBounds(new Rect(0, 0, LauncherButton.ActualWidth, LauncherButton.ActualHeight));

            if (buttonBounds.Contains(screenPoint))
            {
                handled = true;
                return new IntPtr(HtClient);
            }

            handled = true;
            return new IntPtr(HtTransparent);
        }

        private static Point GetPointFromLParam(IntPtr lParam)
        {
            var value = lParam.ToInt64();
            var x = unchecked((short)(value & 0xFFFF));
            var y = unchecked((short)((value >> 16) & 0xFFFF));
            return new Point(x, y);
        }

        private void LauncherButton_Click(object sender, RoutedEventArgs e)
        {
            Clicked?.Invoke(this, EventArgs.Empty);
        }

        private void LauncherButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!LayoutModeEnabled)
            {
                return;
            }

            _isDragging = true;
            _dragOffset = e.GetPosition(this);
            LauncherButton.CaptureMouse();
            e.Handled = true;
        }

        private void LauncherButton_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || !LayoutModeEnabled || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var screen = PointToScreen(e.GetPosition(this));
            Left = screen.X - _dragOffset.X;
            Top = screen.Y - _dragOffset.Y;
            GeometryChanged?.Invoke(this, EventArgs.Empty);
        }

        private void LauncherButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EndDrag();
        }

        private void ResizeHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!LayoutModeEnabled)
            {
                return;
            }

            _isResizing = true;
            _resizeStart = PointToScreen(e.GetPosition(this));
            _startWidth = Width;
            _startHeight = Height;
            ResizeHandle.CaptureMouse();
            e.Handled = true;
        }

        private void ResizeHandle_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isResizing || !LayoutModeEnabled || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var current = PointToScreen(e.GetPosition(this));
            var width = Math.Clamp(_startWidth + (current.X - _resizeStart.X), MinOverlaySize, MaxOverlaySize);
            var height = Math.Clamp(_startHeight + (current.Y - _resizeStart.Y), MinOverlaySize, MaxOverlaySize);
            Width = width;
            Height = height;
            GeometryChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ResizeHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EndResize();
        }

        private void EndDrag()
        {
            if (!_isDragging)
            {
                return;
            }

            _isDragging = false;
            LauncherButton.ReleaseMouseCapture();
            GeometryChanged?.Invoke(this, EventArgs.Empty);
        }

        private void EndResize()
        {
            if (!_isResizing)
            {
                return;
            }

            _isResizing = false;
            ResizeHandle.ReleaseMouseCapture();
            GeometryChanged?.Invoke(this, EventArgs.Empty);
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
