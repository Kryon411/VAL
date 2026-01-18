using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using VAL.Continuum;
using VAL.Continuum.Pipeline.Inject;
using VAL.Host;
using VAL.Host.Abyss;
using VAL.Host.Commands;
using VAL.Host.Portal;

namespace VAL
{
    public partial class MainWindow : Window
    {
        private CoreWebView2? _modulesInitializedForCore = null;
        private int _modulesInitInFlight = 0;

        private DispatcherTimer _continuumTimer = null!;

        private long _lastExitWarnedOperationId = 0;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            try
            {
                if (!OperationCoordinator.IsBusy)
                    return;

                var opId = OperationCoordinator.CurrentOperationId;
                if (opId != 0 && opId == _lastExitWarnedOperationId)
                {
                    // Warned already for this operation; allow exit.
                    OperationCoordinator.RequestCancel();
                    return;
                }

                _lastExitWarnedOperationId = opId;

                var result = MessageBox.Show(
                    "An operation is still running. Exiting may interrupt it.",
                    "VAL",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }

                // Best-effort cancel so guarded operations can stop at safe boundaries.
                OperationCoordinator.RequestCancel();
            }
            catch
            {
                // Never block close due to a guardrail failure.
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Always launch maximized
            WindowState = WindowState.Maximized;

            // ToastHub centralizes toast policy/gating, ToastManager remains the renderer.
            ToastHub.Initialize(this);

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int dark = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            }

            // Profile root (isolated WebView2 user data)
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VAL",
                "Profile"
            );
            Directory.CreateDirectory(userData);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await WebView.EnsureCoreWebView2Async(env);

            WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            WebView.CoreWebView2.Settings.IsWebMessageEnabled = true;


            // ---- Portal runtime (armed-only hotkey + clipboard staging) ----
            try
            {
                PortalRuntime.Initialize(
                    postJson: (json) => { try { WebView.CoreWebView2.PostWebMessageAsJson(json); } catch { } },
                    focusWebView: () => { try { WebView.Focus(); } catch { } try { _ = WebView.CoreWebView2.ExecuteScriptAsync("(()=>{try{const selectors=['form textarea','textarea[placeholder]','div[contenteditable=\"true\"][role=\"textbox\"]','div.ProseMirror[contenteditable=\"true\"]','div[contenteditable=\"true\"][data-slate-editor=\"true\"]'];for(const s of selectors){  const el=document.querySelector(s);  if(el){ try{ el.focus(); }catch{}; try{ el.click(); }catch{}; return true; }}// fallback: find any visible contenteditable in the bottom composer regionconst cands=[...document.querySelectorAll('div[contenteditable=\"true\"]')].filter(e=>{  const r=e.getBoundingClientRect();  return r.width>100 && r.height>20 && r.bottom> (window.innerHeight*0.55);});if(cands.length){  const el=cands[cands.length-1];  try{ el.focus(); }catch{}; try{ el.click(); }catch{}; return true;}}catch(e){} return false;})()"); } catch { } }
                );

                this.SourceInitialized += (_, __) =>
                {
                    try
                    {
                        var hwnd = new WindowInteropHelper(this).Handle;
                        if (hwnd != IntPtr.Zero) PortalRuntime.AttachWindow(hwnd);
                    }
                    catch { }
                };
            }
            catch { }

            WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(11, 12, 16);

            // Continuum seed dispatch timer (100ms).
            _continuumTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _continuumTimer.Tick += ContinuumTimer_Tick;
            _continuumTimer.Start();

            WebView.CoreWebView2.NavigationCompleted += async (_, __) =>
            {
                try { await EnsureModulesInitializedAsync(); } catch { }
            };

            WebView.CoreWebView2.WebMessageReceived += (_, e3) =>
            {
                // Single router for all WebView -> Host messages.
                HostCommandRouter.Handle(e3.WebMessageAsJson);
            };


            // Keep all auth popups / window.open navigations inside the same WebView instance.
            // This prevents the login flow from spawning an un-initialized WebView where VAL modules are missing.
            WebView.CoreWebView2.NewWindowRequested += (_, e4) =>
            {
                try
                {
                    e4.NewWindow = WebView.CoreWebView2;
                    e4.Handled = true;
                }
                catch
                {
                    try
                    {
                        e4.Handled = true;

                        var uri = e4.Uri;
                        if (!string.IsNullOrWhiteSpace(uri))
                            WebView.CoreWebView2.Navigate(uri);
                    }
                    catch
                    {
                        // Never let window routing break the host.
                    }
                }
            };

            // Best-effort init: register module scripts as early as possible so first-run login flows
            // still get VAL UI without requiring an app restart.
            _ = EnsureModulesInitializedAsync();

            WebView.Source = new Uri("https://chatgpt.com");
        }

        private async Task EnsureModulesInitializedAsync()
        {
            var core = WebView?.CoreWebView2;
            if (core == null) return;

            // Per-Core guard: if WebView2 re-initializes (rare) or a login flow attempts to spawn a new instance,
            // we allow module registration once per CoreWebView2.
            if (ReferenceEquals(_modulesInitializedForCore, core))
                return;

            if (Interlocked.CompareExchange(ref _modulesInitInFlight, 1, 0) != 0)
                return;

            try
            {
                await ModuleLoader.Initialize(core);
                _modulesInitializedForCore = core;

                // Allow ContinuumHost to post command messages back into the WebView (e.g., capture flush preflight).
                ContinuumHost.PostToWebJson = (json) =>
                {
                    try { core.PostWebMessageAsJson(json); } catch { }
                };

                // Abyss uses the same host -> webview post channel as other modules.
                AbyssRuntime.Initialize(
                    postJson: (json) => { try { core.PostWebMessageAsJson(json); } catch { } }
                );
            }
            catch
            {
                // Keep host resilient; allow retry on the next navigation.
                _modulesInitializedForCore = null;
            }
            finally
            {
                Interlocked.Exchange(ref _modulesInitInFlight, 0);
            }
        }
        private void ContinuumTimer_Tick(object? sender, EventArgs e)
        {
            if (WebView?.CoreWebView2 == null) return;

            var seed = EssenceInjectQueue.Dequeue();
            if (seed != null)
            {
                try
                {
                    var payload = new
                    {
                        type = "continuum.inject_text",
                        chatId = seed.ChatId,
                        mode = seed.Mode,
                        text = seed.EssenceText,
                        sourceFile = seed.SourceFileName,
                        essenceFile = seed.EssenceFileName,
                        openNewChat = seed.OpenNewChat
                    };

                    var json = JsonSerializer.Serialize(payload);
                    WebView.CoreWebView2.PostWebMessageAsJson(json);
                }
                catch
                {
                    // Telemetry only; ignore.
                }
            }
        }

    }
}
