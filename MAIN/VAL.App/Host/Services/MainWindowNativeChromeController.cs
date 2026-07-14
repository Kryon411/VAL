using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

using VAL.Host;

namespace VAL.App.Host.Services
{
    public sealed class MainWindowNativeChromeController
    {
        private const int DwmUseImmersiveDarkMode = 20;
        private const int LayoutToggleHotKeyId = 0x4256;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const int VKeyL = 0x4C;
        private const int WmHotKey = 0x0312;

        private readonly ILog _log;
        private bool _hotKeyRegistered;
        private HwndSource? _hwndSource;
        private IntPtr _hwnd;

        public MainWindowNativeChromeController(ILog log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public event EventHandler? LayoutToggleRequested;

        public void Attach(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || _hwnd == hwnd)
            {
                return;
            }

            Detach();

            _hwnd = hwnd;
            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(WndProc);
        }

        public void Detach()
        {
            UnregisterLayoutHotKey();

            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }

            _hwnd = IntPtr.Zero;
        }

        public void ApplyImmersiveDarkMode()
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            try
            {
                int enabled = 1;
                var hr = DwmSetWindowAttribute(_hwnd, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
                if (hr != 0)
                {
                    _log.Warn(nameof(MainWindowNativeChromeController), $"Failed to set dark mode attribute (HRESULT=0x{hr:X8}).");
                }
            }
            catch
            {
                _log.Warn(nameof(MainWindowNativeChromeController), "Failed to apply immersive dark mode.");
            }
        }

        public void RegisterLayoutHotKey()
        {
            if (_hotKeyRegistered || _hwnd == IntPtr.Zero)
            {
                return;
            }

            var registered = RegisterHotKey(_hwnd, LayoutToggleHotKeyId, ModControl | ModAlt | ModShift, VKeyL);
            if (!registered)
            {
                _log.Warn(nameof(MainWindowNativeChromeController), "Failed to register layout hotkey Ctrl+Alt+Shift+L.");
                return;
            }

            _hotKeyRegistered = true;
        }

        public void UnregisterLayoutHotKey()
        {
            if (!_hotKeyRegistered || _hwnd == IntPtr.Zero)
            {
                _hotKeyRegistered = false;
                return;
            }

            var unregistered = UnregisterHotKey(_hwnd, LayoutToggleHotKeyId);
            if (!unregistered)
            {
                _log.Warn(nameof(MainWindowNativeChromeController), "Failed to unregister layout hotkey Ctrl+Alt+Shift+L.");
            }

            _hotKeyRegistered = false;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotKey && wParam.ToInt32() == LayoutToggleHotKeyId)
            {
                LayoutToggleRequested?.Invoke(this, EventArgs.Empty);
                handled = true;
            }

            return IntPtr.Zero;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
