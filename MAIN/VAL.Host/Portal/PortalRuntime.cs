using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using VAL.Contracts;
using VAL.Host.Services;
using VAL.Host.WebMessaging;

namespace VAL.Host.Portal
{
    public sealed class PortalRuntime
    {
        private const int HOTKEY_ID = 0xB001;
        private const int WM_HOTKEY = 0x0312;
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        private readonly IWebMessageSender _messageSender;
        private readonly IDockModelService _dockModelService;
        private readonly IDesktopUiContext _uiContext;
        private readonly PortalStaging _staging = new();
        private bool _enabled;
        private bool _privacyAllowed = true;
        private IntPtr _hwnd = IntPtr.Zero;
        private HwndSource? _source;
        private bool _clipboardListenerRegistered;
        private Action? _focusWebView;
        private uint _lastClipSeq;

        // Debounce & dedupe
        private long _lastStageTicks;
        private string _lastSig = "";


// Stronger de-dupe across clipboard churn (Snipping Tool can update clipboard twice for the same pixels).
        private readonly HashSet<string> _recentSigSet = new();
        private readonly Queue<string> _recentSigQueue = new();
        private const int RECENT_SIG_MAX = 24;

        private bool HasRecentSig(string sig)
        {
            if (string.IsNullOrWhiteSpace(sig)) return false;
            return _recentSigSet.Contains(sig);
        }

        private void RememberSig(string sig)
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

        private volatile bool _suppressStage;
        private volatile bool _sending;

        // --- Input constants ---
        private const uint MOD_NOREPEAT = 0x4000;
        private const uint VK_1 = 0x31;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_V = 0x56;
        private const byte VK_LWIN = 0x5B;
        private const byte VK_LSHIFT = 0xA0;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        public PortalRuntime(
            IWebMessageSender messageSender,
            IDockModelService dockModelService,
            IDesktopUiContext uiContext)
        {
            _messageSender = messageSender ?? throw new ArgumentNullException(nameof(messageSender));
            _dockModelService = dockModelService ?? throw new ArgumentNullException(nameof(dockModelService));
            _uiContext = uiContext ?? throw new ArgumentNullException(nameof(uiContext));
        }

        public void Initialize(Action focusWebView)
        {
            _focusWebView = focusWebView ?? throw new ArgumentNullException(nameof(focusWebView));
        }

        public void AttachWindow(IntPtr hwnd)
        {
            _hwnd = hwnd;

            try
            {
                _source = HwndSource.FromHwnd(hwnd);
                _source?.AddHook(WndProc);
            }
            catch { }

            PostDebug($"AttachWindow hwnd=0x{hwnd.ToInt64():X}");

            if (_enabled && _privacyAllowed)
            {
                StartClipboardWatch();
            }
        }

        // Auto-attach for builds where MainWindow.xaml.cs wiring is incomplete:
        private void EnsureAttached()
        {
            if (_hwnd != IntPtr.Zero) return;

            try
            {
                var mw = _uiContext.MainWindow;
                if (mw == null) return;

                var hwnd = new WindowInteropHelper(mw).Handle;
                if (hwnd == IntPtr.Zero) return;

                AttachWindow(hwnd);
            }
            catch { }
        }

        public void SetEnabled(bool enabled)
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
                    _staging.Clear();
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

        public void SetPrivacyAllowed(bool allowed)
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
                    _staging.Clear();
                    _lastSig = "";
                    _recentSigSet.Clear();
                    _recentSigQueue.Clear();
                    PostCleared();
                    PostCount();
                }

                PostEnabledState();
            });
        }

        public void ClearStaging()
        {
            RunOnUI(() =>
            {
                _staging.Clear();
                _lastSig = "";
                _recentSigSet.Clear();
                _recentSigQueue.Clear();
                PostCleared();
                PostCount();
                try { _lastClipSeq = GetClipboardSequenceNumber(); } catch { }
            });
        }

        public void OpenSnipOverlay()
        {
            if (!_enabled || !_privacyAllowed) return;

            PostDebug("OpenSnipOverlay()");
            SendWinShiftS();
        }

        public void SendStaged(int max)
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

                    var items = _staging.Drain(max);
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
                    _staging.Clear();
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

        private void StartClipboardWatch()
        {
            EnsureAttached();

            if (_clipboardListenerRegistered) return;
            if (_hwnd == IntPtr.Zero)
            {
                PostDebug("Clipboard watch skipped: hwnd=0");
                return;
            }

            try
            {
                if (AddClipboardFormatListener(_hwnd))
                {
                    _clipboardListenerRegistered = true;
                    PostDebug("Clipboard watch started");
                }
                else
                {
                    var err = Marshal.GetLastWin32Error();
                    PostDebug($"AddClipboardFormatListener failed err={err}");
                }
            }
            catch (Exception ex)
            {
                PostDebug("Clipboard watch error: " + ex.Message);
            }
        }

        private void PrimeClipboardState()
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


        private void StopClipboardWatch()
        {
            try
            {
                if (_clipboardListenerRegistered && _hwnd != IntPtr.Zero)
                {
                    if (!RemoveClipboardFormatListener(_hwnd))
                    {
                        var err = Marshal.GetLastWin32Error();
                        PostDebug($"RemoveClipboardFormatListener failed err={err}");
                    }
                }
            }
            catch { }
            finally
            {
                _clipboardListenerRegistered = false;
            }

            PostDebug("Clipboard watch stopped");
        }

        private void ProcessClipboardUpdate()
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

                if (_staging.TryAdd(img))
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

        private void PostCount()
        {
            try
            {
                _messageSender.Send(new MessageEnvelope
                {
                    Type = WebMessageTypes.Event,
                    Name = "portal.stage.count",
                    Payload = JsonSerializer.SerializeToElement(new { count = _staging.Count })
                });
            }
            catch { }

            NotifyDockModelState();
        }

        private void PostEnabledState()
        {
            try
            {
                _messageSender.Send(new MessageEnvelope
                {
                    Type = WebMessageTypes.Event,
                    Name = WebCommandNames.PortalState,
                    Payload = JsonSerializer.SerializeToElement(new { enabled = _enabled })
                });
            }
            catch { }

            NotifyDockModelState();
        }

        private void PostCleared()
        {
            try
            {
                _messageSender.Send(new MessageEnvelope
                {
                    Type = WebMessageTypes.Event,
                    Name = "portal.stage.cleared",
                    Payload = JsonSerializer.SerializeToElement(new { })
                });
            }
            catch { }
        }

        private void NotifyDockModelState()
        {
            try
            {
                _dockModelService.UpdatePortalState(_enabled, _privacyAllowed, _staging.Count);
            }
            catch { }
        }

        private void PostDebug(string message)
        {
            try
            {
                _messageSender.Send(new MessageEnvelope
                {
                    Type = WebMessageTypes.Log,
                    Name = "portal.debug",
                    Payload = JsonSerializer.SerializeToElement(new { message = message ?? string.Empty })
                });
            }
            catch { }
        }

        private void RegisterHotkey()
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

        private void UnregisterHotkey()
        {
            if (_hwnd == IntPtr.Zero) return;

            try
            {
                UnregisterHotKey(_hwnd, HOTKEY_ID);
                PostDebug("UnregisterHotKey ok");
            }
            catch { }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
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

            if (msg == WM_CLIPBOARDUPDATE)
            {
                ProcessClipboardUpdate();
            }

            return IntPtr.Zero;
        }

        private void RunOnUI(Action action)
        {
            try
            {
                var dispatcher = _uiContext.Dispatcher;
                if (dispatcher.CheckAccess()) action();
                else dispatcher.Invoke(action);
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hWnd);
    }
}
