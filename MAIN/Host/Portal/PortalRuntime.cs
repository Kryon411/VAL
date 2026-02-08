using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using VAL.Contracts;
using VAL.Host.WebMessaging;

namespace VAL.Host.Portal
{
    internal static class PortalRuntime
    {
        private const int HOTKEY_ID = 0xB001;
        private const int WM_HOTKEY = 0x0312;

        private static bool _enabled;
        private static bool _privacyAllowed = true;
        private static IntPtr _hwnd = IntPtr.Zero;
        private static HwndSource? _source;

        private static DispatcherTimer? _clipTimer;

        private static IWebMessageSender? _messageSender;   // host -> webview
        private static Action? _focusWebView;       // ensure webview focused before paste

        private static uint _lastClipSeq;

        internal static Action<bool, bool, int>? DockModelStateChanged;

        // Debounce & dedupe
        private static long _lastStageTicks;
        private static string _lastSig = "";


// Stronger de-dupe across clipboard churn (Snipping Tool can update clipboard twice for the same pixels).
private static readonly HashSet<string> _recentSigSet = new();
private static readonly Queue<string> _recentSigQueue = new();
private const int RECENT_SIG_MAX = 24;

private static bool HasRecentSig(string sig)
{
    if (string.IsNullOrWhiteSpace(sig)) return false;
    return _recentSigSet.Contains(sig);
}

private static void RememberSig(string sig)
{
    if (string.IsNullOrWhiteSpace(sig)) return;
    if (_recentSigSet.Add(sig))
    {
        _recentSigQueue.Enqueue(sig);
        while (_recentSigQueue.Count > RECENT_SIG_MAX)
        {
            var old = _recentSigQueue.Dequeue();
            _recentSigSet.Remove(old);
        }
    }
}
        private static volatile bool _suppressStage;
        private static volatile bool _sending;

        // --- Input constants ---
        private const uint MOD_NOREPEAT = 0x4000;
        private const uint VK_1 = 0x31;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_V = 0x56;
        private const byte VK_LWIN = 0x5B;
        private const byte VK_LSHIFT = 0xA0;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        public static void Initialize(IWebMessageSender messageSender, Action focusWebView)
        {
            _messageSender = messageSender;
            _focusWebView = focusWebView;
        }

        public static void AttachWindow(IntPtr hwnd)
        {
            _hwnd = hwnd;

            try
            {
                _source = HwndSource.FromHwnd(hwnd);
                _source?.AddHook(WndProc);
            }
            catch { }

            PostDebug($"AttachWindow hwnd=0x{hwnd.ToInt64():X}");
        }

        // Auto-attach for builds where MainWindow.xaml.cs wiring is incomplete:
        private static void EnsureAttached()
        {
            if (_hwnd != IntPtr.Zero) return;

            try
            {
                var mw = Application.Current?.MainWindow;
                if (mw == null) return;

                var hwnd = new WindowInteropHelper(mw).Handle;
                if (hwnd == IntPtr.Zero) return;

                AttachWindow(hwnd);
            }
            catch { }
        }

        public static void SetEnabled(bool enabled)
        {
            RunOnUI(() =>
            {
                if (!_privacyAllowed && enabled)
                {
                    PostDebug("SetEnabled blocked: privacy disabled");
                    enabled = false;
                }

                _enabled = enabled;
                PostDebug("SetEnabled=" + enabled);

                if (enabled)
                {
                    EnsureAttached();
                    RegisterHotkey();
                    PrimeClipboardState();
                    StartClipboardWatch();
                    PostCount();
                }
                else
                {
                    UnregisterHotkey();
                    StopClipboardWatch();
                    PortalStaging.Clear();
                    _lastSig = "";
                    _recentSigSet.Clear();
                    _recentSigQueue.Clear();
                    PostCleared();
                    // UI polish: immediately reset the counter to 0 when Portal is disarmed.
                    PostCount();
                }

                PostEnabledState();
            });
        }

        public static void SetPrivacyAllowed(bool allowed)
        {
            RunOnUI(() =>
            {
                _privacyAllowed = allowed;
                if (!allowed && _enabled)
                {
                    PostDebug("Privacy disabled: forcing Portal off");
                    _enabled = false;
                    UnregisterHotkey();
                    StopClipboardWatch();
                    PortalStaging.Clear();
                    _lastSig = "";
                    _recentSigSet.Clear();
                    _recentSigQueue.Clear();
                    PostCleared();
                    PostCount();
                }

                PostEnabledState();
            });
        }

        public static void ClearStaging()
        {
            RunOnUI(() =>
            {
                PortalStaging.Clear();
                _lastSig = "";
                _recentSigSet.Clear();
                _recentSigQueue.Clear();
                PostCleared();
                PostCount();
                try { _lastClipSeq = GetClipboardSequenceNumber(); } catch { }
            });
        }

        public static void OpenSnipOverlay()
        {
            if (!_enabled || !_privacyAllowed) return;

            PostDebug("OpenSnipOverlay()");
            SendWinShiftS();
        }

        public static void SendStaged(int max)
        {
            if (!_enabled || !_privacyAllowed) return;
            if (_sending) { PostDebug("SendStaged ignored: already sending"); return; }

            RunOnUI(async () =>
            {
                _sending = true;
                _suppressStage = true;

                try
                {
                    max = Math.Max(1, Math.Min(10, max));

                    // Focus the webview/composer before pasting.
                    _focusWebView?.Invoke();
                    await Task.Delay(120);

                    var items = PortalStaging.Drain(max);
                    PostDebug($"SendStaged: draining {items.Length}");

                    if (items.Length == 0)
                    {
                        PostDebug("SendStaged: nothing staged");
                        return;
                    }

                    // Paste each staged image with conservative delays + a retry.
                    for (int i = 0; i < items.Length; i++)
                    {
                        var img = items[i];
                        try
                        {
                            Clipboard.SetImage(img);
                        }
                        catch (Exception ex)
                        {
                            PostDebug("Clipboard.SetImage failed: " + ex.Message);
                            continue;
                        }

                        // Let clipboard settle.
                        await Task.Delay(180);

                        // First paste attempt.
                        _focusWebView?.Invoke();
                        await Task.Delay(40);
                        SendCtrlV();

                        // Let the web app ingest the image.
                        await Task.Delay(450);                        await Task.Delay(260);
                    }

                    // After sending, clear + reset counters.
                    PortalStaging.Clear();
                    _recentSigSet.Clear();
                    _recentSigQueue.Clear();
                    PostCleared();

                    // Reset clipboard sequence to avoid instant re-stage when we were manipulating it.
                    try { _lastClipSeq = GetClipboardSequenceNumber(); } catch { }
                }
                catch (Exception ex)
                {
                    PostDebug("SendStaged exception: " + ex.Message);
                }
                finally
                {
                    // Small delay before allowing staging again (helps avoid double-count from post-send clipboard churn).
                    await Task.Delay(500);
                    _suppressStage = false;
                    _sending = false;
                }
            });
        }

        private static void StartClipboardWatch()
        {
            if (_clipTimer != null) return;

            _clipTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };

            _clipTimer.Tick += (_, __) =>
            {
                if (!_enabled) return;
                if (_suppressStage) return;

                try
                {
                    var seq = GetClipboardSequenceNumber();
                    if (seq == _lastClipSeq) return;
                    _lastClipSeq = seq;

                    if (!Clipboard.ContainsImage()) return;

                    var img = Clipboard.GetImage();
                    if (img == null) return;

                    // Debounce: Snipping Tool often updates clipboard twice in quick succession.
                    var now = DateTime.UtcNow.Ticks;
                    if (now - _lastStageTicks < TimeSpan.FromMilliseconds(850).Ticks) return;

                    // Dedupe: ignore identical image signatures (common when clipboard sequence bumps twice).
                    var sig = ComputeSignature(img);
                    if (sig.Length > 0 && sig == _lastSig) return;

                    if (HasRecentSig(sig)) return;

                    if (PortalStaging.TryAdd(img))
                    {
                        _lastStageTicks = now;
                        _lastSig = sig;
                        RememberSig(sig);
                        PostCount();
                    }
                }
                catch (Exception ex)
                {
                    PostDebug("Clipboard watch error: " + ex.Message);
                }
            };

            _clipTimer.Start();
            PostDebug("Clipboard watch started");
        }

        private static void PrimeClipboardState()
        {
            try { _lastClipSeq = GetClipboardSequenceNumber(); } catch { }

            try
            {
                if (Clipboard.ContainsImage())
                {
                    var img = Clipboard.GetImage();
                    if (img != null)
                    {
                        _lastSig = ComputeSignature(img);
                        _lastStageTicks = DateTime.UtcNow.Ticks;
                        RememberSig(_lastSig);
                    }
                }
            }
            catch { }
        }


        private static void StopClipboardWatch()
        {
            try
            {
                _clipTimer?.Stop();
                _clipTimer = null;
            }
            catch { }

            PostDebug("Clipboard watch stopped");
        }

        private static string ComputeSignature(BitmapSource img)
        {
            try
            {
                // Normalize format to avoid false “different” signatures for the same pixels.
                BitmapSource src = img;
                if (src.Format != System.Windows.Media.PixelFormats.Bgra32)
                {
                    var conv = new FormatConvertedBitmap();
                    conv.BeginInit();
                    conv.Source = src;
                    conv.DestinationFormat = System.Windows.Media.PixelFormats.Bgra32;
                    conv.EndInit();
                    conv.Freeze();
                    src = conv;
                }

                int w = src.PixelWidth;
                int h = src.PixelHeight;
                int stride = w * 4;

                int rows = Math.Min(8, h);
                if (rows <= 0 || w <= 0) return "";

                // Sample top and bottom blocks.
                int blockBytes = Math.Min(stride * rows, 32768);
                var bufTop = new byte[blockBytes];
                var bufBot = new byte[blockBytes];

                src.CopyPixels(new Int32Rect(0, 0, w, rows), bufTop, stride, 0);

                int yBot = Math.Max(0, h - rows);
                src.CopyPixels(new Int32Rect(0, yBot, w, rows), bufBot, stride, 0);

                using var sha = System.Security.Cryptography.SHA256.Create();
                sha.TransformBlock(BitConverter.GetBytes(w), 0, 4, null, 0);
                sha.TransformBlock(BitConverter.GetBytes(h), 0, 4, null, 0);
                sha.TransformBlock(bufTop, 0, bufTop.Length, null, 0);
                sha.TransformFinalBlock(bufBot, 0, bufBot.Length);

                return Convert.ToBase64String(sha.Hash ?? Array.Empty<byte>());
            }
            catch
            {
                return "";
            }
        }

        private static void PostCount()
        {
            try
            {
                _messageSender?.Send(new MessageEnvelope
                {
                    Type = WebMessageTypes.Event,
                    Name = "portal.stage.count",
                    Payload = JsonSerializer.SerializeToElement(new { count = PortalStaging.Count })
                });
            }
            catch { }

            NotifyDockModelState();
        }

        private static void PostEnabledState()
        {
            try
            {
                _messageSender?.Send(new MessageEnvelope
                {
                    Type = WebMessageTypes.Event,
                    Name = WebCommandNames.PortalState,
                    Payload = JsonSerializer.SerializeToElement(new { enabled = _enabled })
                });
            }
            catch { }

            NotifyDockModelState();
        }

        private static void PostCleared()
        {
            try
            {
                _messageSender?.Send(new MessageEnvelope
                {
                    Type = WebMessageTypes.Event,
                    Name = "portal.stage.cleared",
                    Payload = JsonSerializer.SerializeToElement(new { })
                });
            }
            catch { }
        }

        private static void NotifyDockModelState()
        {
            try
            {
                DockModelStateChanged?.Invoke(_enabled, _privacyAllowed, PortalStaging.Count);
            }
            catch { }
        }

        private static void PostDebug(string message)
        {
            try
            {
                _messageSender?.Send(new MessageEnvelope
                {
                    Type = WebMessageTypes.Log,
                    Name = "portal.debug",
                    Payload = JsonSerializer.SerializeToElement(new { message = message ?? string.Empty })
                });
            }
            catch { }
        }

        private static void RegisterHotkey()
        {
            EnsureAttached();

            if (_hwnd == IntPtr.Zero)
            {
                PostDebug("RegisterHotKey skipped: hwnd=0");
                return;
            }

            try
            {
                var ok = RegisterHotKey(_hwnd, HOTKEY_ID, MOD_NOREPEAT, VK_1);
                if (!ok)
                {
                    var err = Marshal.GetLastWin32Error();
                    PostDebug($"RegisterHotKey(MOD_NOREPEAT) failed err={err}; retrying modifiers=0");

                    ok = RegisterHotKey(_hwnd, HOTKEY_ID, 0, VK_1);
                    if (!ok)
                    {
                        err = Marshal.GetLastWin32Error();
                        PostDebug($"RegisterHotKey(mod=0) failed err={err}");
                    }
                    else
                    {
                        PostDebug("RegisterHotKey(mod=0) ok");
                    }
                }
                else
                {
                    PostDebug("RegisterHotKey(MOD_NOREPEAT) ok");
                }
            }
            catch (Exception ex)
            {
                PostDebug("RegisterHotKey exception: " + ex.Message);
            }
        }

        private static void UnregisterHotkey()
        {
            if (_hwnd == IntPtr.Zero) return;

            try
            {
                UnregisterHotKey(_hwnd, HOTKEY_ID);
                PostDebug("UnregisterHotKey ok");
            }
            catch { }
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                try
                {
                    if (wParam.ToInt32() == HOTKEY_ID)
                    {
                        handled = true;
                        PostDebug("HOTKEY fired");
                        OpenSnipOverlay();
                        return IntPtr.Zero;
                    }
                }
                catch { }
            }

            return IntPtr.Zero;
        }

        private static void RunOnUI(Action action)
        {
            try
            {
                var app = Application.Current;
                if (app?.Dispatcher == null)
                {
                    action();
                    return;
                }

                if (app.Dispatcher.CheckAccess()) action();
                else app.Dispatcher.Invoke(action);
            }
            catch
            {
                try { action(); } catch { }
            }
        }

        // --- Input simulation ---
        private static void SendCtrlV()
        {
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private static void SendWinShiftS()
        {
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_LSHIFT, 0, 0, UIntPtr.Zero);
            keybd_event((byte)'S', 0, 0, UIntPtr.Zero);

            keybd_event((byte)'S', 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LSHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();
    }
}
