using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Options;
using Microsoft.Web.WebView2.Core;
using VAL.Host.Options;
using VAL.Host.WebMessaging;

namespace VAL.Host.Services
{
    public sealed class SmokeTestRunner
    {
        private static readonly string[] ExpectedModules =
        {
            "Dock",
            "Continuum",
            "Abyss",
            "Portal",
            "VALTheme",
            "Void"
        };

        private readonly SmokeTestSettings _settings;
        private readonly IAppPaths _appPaths;
        private readonly IBuildInfo _buildInfo;
        private readonly IWebViewRuntime _webViewRuntime;
        private readonly IModuleRuntimeService _moduleRuntimeService;
        private readonly IWebMessageSender _webMessageSender;
        private readonly ICommandDispatcher _commandDispatcher;
        private readonly IUiThread _uiThread;
        private readonly IOptions<ValOptions> _valOptions;
        private readonly IOptions<WebViewOptions> _webViewOptions;
        private readonly IOptions<ModuleOptions> _moduleOptions;
        private int _reportWritten;

        public SmokeTestRunner(
            SmokeTestSettings settings,
            IAppPaths appPaths,
            IBuildInfo buildInfo,
            IWebViewRuntime webViewRuntime,
            IModuleRuntimeService moduleRuntimeService,
            IWebMessageSender webMessageSender,
            ICommandDispatcher commandDispatcher,
            IUiThread uiThread,
            IOptions<ValOptions> valOptions,
            IOptions<WebViewOptions> webViewOptions,
            IOptions<ModuleOptions> moduleOptions)
        {
            _settings = settings;
            _appPaths = appPaths;
            _buildInfo = buildInfo;
            _webViewRuntime = webViewRuntime;
            _moduleRuntimeService = moduleRuntimeService;
            _webMessageSender = webMessageSender;
            _commandDispatcher = commandDispatcher;
            _uiThread = uiThread;
            _valOptions = valOptions;
            _webViewOptions = webViewOptions;
            _moduleOptions = moduleOptions;
        }

        public void Register(Application app, SmokeTestState state)
        {
            if (!_settings.Enabled)
                return;

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                var exception = args.ExceptionObject as Exception ?? new Exception("Unhandled exception.");
                WriteCrashReport(exception, "AppDomain");
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                WriteCrashReport(args.Exception, "TaskScheduler");
                args.SetObserved();
            };

            app.DispatcherUnhandledException += (_, args) =>
            {
                WriteCrashReport(args.Exception, "Dispatcher");
            };

            app.Startup += (_, __) => _ = RunAsync(app, state);
        }

        private async Task RunAsync(Application app, SmokeTestState state)
        {
            SmokeTestResult result;

            try
            {
                result = await ExecuteAsync();
            }
            catch (Exception ex)
            {
                result = SmokeTestResult.FromException(ex, ResolveReportPath());
                TryWriteReport(result);
            }

            state.Completion.TrySetResult(result.ExitCode);
            Environment.ExitCode = result.ExitCode;

            try
            {
                _uiThread.Invoke(() => app.Shutdown());
            }
            catch
            {
                // Ignore shutdown errors.
            }
        }

        private async Task<SmokeTestResult> ExecuteAsync()
        {
            var reportPath = ResolveReportPath();
            var isCi = IsCiEnvironment();
            var result = new SmokeTestResult(reportPath)
            {
                StartTime = DateTimeOffset.Now
            };

            using var cts = new CancellationTokenSource(_settings.Timeout);

            try
            {
                ValidateOptions(result);
                ValidateLogSystem(result);
                ValidateModulesRoot(result);

                result.AvailableWebViewRuntimeVersion = TryGetWebViewRuntimeVersion(out var runtimeException);
                if (!isCi && runtimeException != null)
                {
                    throw new SmokeTestFailureException(20, "WebView2 runtime is missing.", runtimeException);
                }

                if (isCi)
                {
                    result.WebViewCheckSkipped = true;
                }
                else
                {
                    await WaitForWebViewAsync(result, cts.Token);
                    await WaitForModulesAsync(result, cts.Token);
                    ValidateRouter(result);
                }

                result.Passed = true;
                result.ExitCode = 0;
            }
            catch (OperationCanceledException)
            {
                result.Passed = false;
                result.ExitCode = 10;
                result.FailureReason = $"Smoke test timed out after {_settings.Timeout.TotalMilliseconds} ms.";
            }
            catch (SmokeTestFailureException ex)
            {
                result.Passed = false;
                result.ExitCode = ex.ExitCode;
                result.FailureReason = ex.Message;
                result.Exception = ex.InnerException;
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.ExitCode = 40;
                result.FailureReason = "Unexpected smoke test failure.";
                result.Exception = ex;
            }
            finally
            {
                result.EndTime = DateTimeOffset.Now;
                result.ModuleStatuses = ModuleLoader.GetModuleStatuses();
                TryWriteReport(result);
            }

            return result;
        }

        private void ValidateOptions(SmokeTestResult result)
        {
            if (_valOptions?.Value == null || _webViewOptions?.Value == null || _moduleOptions?.Value == null)
            {
                throw new SmokeTestFailureException(40, "Required options are not loaded.");
            }

            result.ValOptions = _valOptions.Value;
            result.WebViewOptions = _webViewOptions.Value;
            result.ModuleOptions = _moduleOptions.Value;
        }

        private void ValidateLogSystem(SmokeTestResult result)
        {
            var logPath = Path.Combine(_appPaths.LogsRoot, "VAL.log");
            var line = $"{DateTimeOffset.Now:O} [SMOKE] Smoke test log write.{Environment.NewLine}";

            try
            {
                Directory.CreateDirectory(_appPaths.LogsRoot);
                File.AppendAllText(logPath, line, Encoding.UTF8);
                result.LogAppendSucceeded = true;
            }
            catch (Exception ex)
            {
                result.LogAppendSucceeded = false;
                throw new SmokeTestFailureException(40, "Failed to append to log file.", ex);
            }
        }

        private void ValidateModulesRoot(SmokeTestResult result)
        {
            if (string.IsNullOrWhiteSpace(_appPaths.ModulesRoot))
            {
                throw new SmokeTestFailureException(30, "ModulesRoot is not configured.");
            }

            if (!Directory.Exists(_appPaths.ModulesRoot))
            {
                throw new SmokeTestFailureException(30, $"ModulesRoot does not exist: {_appPaths.ModulesRoot}");
            }

            try
            {
                var hasModuleFiles = Directory
                    .EnumerateFiles(_appPaths.ModulesRoot, "*.module.json", SearchOption.AllDirectories)
                    .Any();

                if (!hasModuleFiles)
                {
                    throw new SmokeTestFailureException(30, $"No module manifests found under {_appPaths.ModulesRoot}");
                }
            }
            catch (SmokeTestFailureException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new SmokeTestFailureException(30, $"Failed to inspect ModulesRoot: {_appPaths.ModulesRoot}", ex);
            }

            result.ModulesRootReady = true;
        }

        private async Task WaitForWebViewAsync(SmokeTestResult result, CancellationToken token)
        {
            var pollInterval = TimeSpan.FromMilliseconds(200);

            while (_webViewRuntime.Core == null)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(pollInterval, token);
            }

            result.WebViewReady = true;
            result.WebViewRuntimeVersion = _webViewRuntime.Core?.Environment?.BrowserVersionString;

            if (_webViewRuntime.Core == null)
            {
                throw new SmokeTestFailureException(20, "WebView2 failed to initialize.");
            }

            try
            {
                await _moduleRuntimeService.EnsureModulesInitializedAsync();
            }
            catch (Exception ex)
            {
                throw new SmokeTestFailureException(30, "Module initialization failed.", ex);
            }
        }

        private async Task WaitForModulesAsync(SmokeTestResult result, CancellationToken token)
        {
            var pollInterval = TimeSpan.FromMilliseconds(250);
            var expected = new HashSet<string>(ExpectedModules, StringComparer.OrdinalIgnoreCase);

            while (true)
            {
                token.ThrowIfCancellationRequested();

                var statuses = ModuleLoader.GetModuleStatuses();
                result.ModuleStatuses = statuses;

                var expectedStatuses = statuses
                    .Where(status => expected.Contains(status.Name))
                    .ToList();

                if (expectedStatuses.Count == expected.Count)
                {
                    var failures = expectedStatuses
                        .Where(status => !string.Equals(status.Status, "Loaded", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (failures.Count == 0)
                    {
                        return;
                    }

                    var failureNames = string.Join(", ", failures.Select(failure => $"{failure.Name} ({failure.Status})"));
                    throw new SmokeTestFailureException(30, $"Modules failed to load: {failureNames}.");
                }

                await Task.Delay(pollInterval, token);
            }
        }

        private void ValidateRouter(SmokeTestResult result)
        {
            if (_commandDispatcher == null)
            {
                throw new SmokeTestFailureException(40, "Command dispatcher is not available.");
            }

            if (_webMessageSender == null)
            {
                throw new SmokeTestFailureException(40, "Web message sender is not available.");
            }

            result.MessageSenderWired = Continuum.ContinuumHost.PostToWebMessage != null;

            if (!result.MessageSenderWired)
            {
                throw new SmokeTestFailureException(40, "Web message sender is not wired to ContinuumHost.");
            }
        }

        private string ResolveReportPath()
        {
            if (!string.IsNullOrWhiteSpace(_settings.ReportPath))
            {
                return _settings.ReportPath;
            }

            return Path.Combine(_appPaths.LogsRoot, "SmokeReport.txt");
        }

        private static string? TryGetWebViewRuntimeVersion(out Exception? exception)
        {
            exception = null;

            try
            {
                var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                return string.IsNullOrWhiteSpace(version) ? null : version;
            }
            catch (Exception ex)
            {
                exception = ex;
                return null;
            }
        }

        private static bool IsCiEnvironment()
        {
            return string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);
        }

        private void WriteCrashReport(Exception exception, string source)
        {
            var report = new SmokeTestResult(ResolveReportPath())
            {
                Passed = false,
                ExitCode = 40,
                FailureReason = $"Unhandled exception from {source}.",
                Exception = exception,
                StartTime = DateTimeOffset.Now,
                EndTime = DateTimeOffset.Now,
                ModuleStatuses = ModuleLoader.GetModuleStatuses(),
                ValOptions = _valOptions.Value,
                WebViewOptions = _webViewOptions.Value,
                ModuleOptions = _moduleOptions.Value,
                WebViewRuntimeVersion = _webViewRuntime.Core?.Environment?.BrowserVersionString,
                AvailableWebViewRuntimeVersion = TryGetWebViewRuntimeVersion(out _)
            };

            TryWriteReport(report);
        }

        private void TryWriteReport(SmokeTestResult result)
        {
            if (Interlocked.Exchange(ref _reportWritten, 1) == 1)
                return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(result.ReportPath) ?? ".");
                File.WriteAllText(result.ReportPath, BuildReport(result));
            }
            catch
            {
                // Never throw while writing the smoke report.
            }
        }

        private string BuildReport(SmokeTestResult result)
        {
            var builder = new StringBuilder();

            builder.AppendLine("VAL Smoke Test Report");
            builder.AppendLine($"Result: {(result.Passed ? "PASS" : "FAIL")}");
            builder.AppendLine($"ExitCode: {result.ExitCode}");
            builder.AppendLine($"Started: {result.StartTime:u}");
            builder.AppendLine($"Finished: {result.EndTime:u}");

            if (!string.IsNullOrWhiteSpace(result.FailureReason))
            {
                builder.AppendLine($"FailureReason: {result.FailureReason}");
            }

            if (result.Exception != null)
            {
                builder.AppendLine("Exception:");
                builder.AppendLine(result.Exception.ToString());
            }

            builder.AppendLine();
            builder.AppendLine("BuildInfo");
            builder.AppendLine($"Version: {_buildInfo.Version}");
            builder.AppendLine($"InformationalVersion: {_buildInfo.InformationalVersion}");
            builder.AppendLine($"Environment: {_buildInfo.Environment}");
            builder.AppendLine($"BuildDate: {_buildInfo.BuildDate}");
            builder.AppendLine($"GitSha: {_buildInfo.GitSha}");

            builder.AppendLine();
            builder.AppendLine("Paths");
            builder.AppendLine($"ContentRoot: {_appPaths.ContentRoot}");
            builder.AppendLine($"DataRoot: {_appPaths.DataRoot}");
            builder.AppendLine($"LogsRoot: {_appPaths.LogsRoot}");
            builder.AppendLine($"ModulesRoot: {_appPaths.ModulesRoot}");
            builder.AppendLine($"ProfileRoot: {_appPaths.ProfileRoot}");
            builder.AppendLine($"SmokeReportPath: {result.ReportPath}");

            builder.AppendLine();
            builder.AppendLine("Options");
            builder.AppendLine($"ValOptionsLoaded: {result.ValOptions != null}");
            builder.AppendLine($"WebViewOptionsLoaded: {result.WebViewOptions != null}");
            builder.AppendLine($"ModuleOptionsLoaded: {result.ModuleOptions != null}");
            builder.AppendLine($"LogAppendSucceeded: {result.LogAppendSucceeded}");

            builder.AppendLine();
            builder.AppendLine("WebView2");
            builder.AppendLine($"CoreReady: {result.WebViewReady}");
            builder.AppendLine($"RuntimeVersion: {result.WebViewRuntimeVersion}");
            builder.AppendLine($"AvailableRuntimeVersion: {result.AvailableWebViewRuntimeVersion}");
            if (result.WebViewCheckSkipped)
            {
                builder.AppendLine("WebView2Check: SKIPPED (CI)");
            }

            builder.AppendLine();
            builder.AppendLine("Router");
            builder.AppendLine($"CommandDispatcherAvailable: {_commandDispatcher != null}");
            builder.AppendLine($"WebMessageSenderAvailable: {_webMessageSender != null}");
            builder.AppendLine($"MessageSenderWired: {result.MessageSenderWired}");

            builder.AppendLine();
            builder.AppendLine("Module Statuses");

            foreach (var status in result.ModuleStatuses ?? new List<ModuleLoader.ModuleStatusInfo>())
            {
                builder.AppendLine($"- {status.Name} | {status.Status} | {status.Path}");
            }

            return builder.ToString();
        }

        private sealed class SmokeTestFailureException : Exception
        {
            public SmokeTestFailureException(int exitCode, string message, Exception? inner = null)
                : base(message, inner)
            {
                ExitCode = exitCode;
            }

            public int ExitCode { get; }
        }

        private sealed class SmokeTestResult
        {
            public SmokeTestResult(string reportPath)
            {
                ReportPath = reportPath;
            }

            public bool Passed { get; set; }
            public int ExitCode { get; set; }
            public string? FailureReason { get; set; }
            public Exception? Exception { get; set; }
            public DateTimeOffset StartTime { get; set; }
            public DateTimeOffset EndTime { get; set; }
            public string ReportPath { get; }
            public string? WebViewRuntimeVersion { get; set; }
            public string? AvailableWebViewRuntimeVersion { get; set; }
            public bool WebViewReady { get; set; }
            public bool WebViewCheckSkipped { get; set; }
            public bool LogAppendSucceeded { get; set; }
            public bool MessageSenderWired { get; set; }
            public bool ModulesRootReady { get; set; }
            public IReadOnlyList<ModuleLoader.ModuleStatusInfo>? ModuleStatuses { get; set; }
            public ValOptions? ValOptions { get; set; }
            public WebViewOptions? WebViewOptions { get; set; }
            public ModuleOptions? ModuleOptions { get; set; }

            public static SmokeTestResult FromException(Exception ex, string reportPath)
            {
                return new SmokeTestResult(reportPath)
                {
                    Passed = false,
                    ExitCode = 40,
                    FailureReason = "Unexpected smoke test failure.",
                    Exception = ex,
                    StartTime = DateTimeOffset.Now,
                    EndTime = DateTimeOffset.Now
                };
            }
        }
    }
}
