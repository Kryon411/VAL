using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using VAL.Host.Json;
using VAL.Host.Logging;
using VAL.Host.Options;

namespace VAL.Host
{
    /// <summary>
    /// Loads *.module.json UI modules into the WebView2 instance.
    ///
    /// Layout:
    /// - Dock\Dock.module.json (core UI)
    /// - Modules\**\*.module.json (feature modules)
    ///
    /// Notes:
    /// - Modules may specify either "entry" or a "scripts" array. If "scripts" is present, it will be
    ///   loaded in order (with "entry" ensured as well).
    /// - No UI toasts are emitted here (kept quiet; use devtools console for troubleshooting).
    /// </summary>
    public static class ModuleLoader
    {
        private const string SupportedApiVersion = "1";
        private static readonly ModuleRegistrationTracker _registrationTracker = new();
        private static readonly object _statusLock = new();
        private static readonly Dictionary<string, ModuleStatusInfo> _moduleStatuses = new(StringComparer.OrdinalIgnoreCase);
        private static readonly RateLimiter RateLimiter = new();
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);
        private static readonly HashSet<string> SupportedCapabilities = new(StringComparer.OrdinalIgnoreCase)
        {
            "ui"
        };

        public sealed class ModuleStatusInfo
        {
            public ModuleStatusInfo(string name, string status, string path)
            {
                Name = name;
                Status = status;
                Path = path;
            }

            public string Name { get; }
            public string Status { get; }
            public string Path { get; }
        }

        private sealed class ModuleManifest
        {
            public string? name { get; set; }
            public string? version { get; set; }
            public string? apiVersion { get; set; }
            public string? hostMinVersion { get; set; }
            public string? minHostVersion { get; set; }
            public bool? enabled { get; set; }
            public string[]? entryScripts { get; set; }
            public string[]? styles { get; set; }
            public string[]? capabilities { get; set; }
        }

        public static IReadOnlyList<ModuleStatusInfo> GetModuleStatuses()
        {
            lock (_statusLock)
            {
                return _moduleStatuses.Values
                    .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(status => status.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        private static void RecordModuleStatus(string configPath, string moduleName, string status)
        {
            if (string.IsNullOrWhiteSpace(configPath) || string.IsNullOrWhiteSpace(moduleName))
                return;

            lock (_statusLock)
            {
                _moduleStatuses[configPath] = new ModuleStatusInfo(moduleName, status, configPath);
            }
        }

        private static string FormatConfigExceptionDetails(Exception exception)
        {
            if (exception is JsonException jsonException)
            {
                var message = jsonException.Message;
                if (jsonException.LineNumber.HasValue || jsonException.BytePositionInLine.HasValue)
                {
                    var line = jsonException.LineNumber?.ToString(CultureInfo.InvariantCulture) ?? "?";
                    var position = jsonException.BytePositionInLine?.ToString(CultureInfo.InvariantCulture) ?? "?";
                    message += $" (Line {line}, Position {position})";
                }

                return message;
            }

            return exception.Message;
        }

        private static string ResolveContentRoot()
        {
            try
            {
                var exePath = Environment.ProcessPath;
                var exeDir = string.IsNullOrWhiteSpace(exePath) ? null : Path.GetDirectoryName(exePath);
                var candidate = exeDir ?? AppContext.BaseDirectory;
                return Path.GetFullPath(candidate);
            }
            catch
            {
                return AppContext.BaseDirectory;
            }
        }

        private static Version? ParseVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;

            var trimmed = version.Trim();
            var metadataIndex = trimmed.IndexOfAny(new[] { '-', '+' });
            if (metadataIndex > 0)
                trimmed = trimmed.Substring(0, metadataIndex);

            return Version.TryParse(trimmed, out var parsed) ? parsed : null;
        }

        public static async Task Initialize(
            CoreWebView2 core,
            string? modulesRoot,
            string? contentRoot,
            ModuleOptions? moduleOptions = null,
            string? hostVersion = null)
        {
            if (core == null)
                return;

            var hostVersionParsed = ParseVersion(hostVersion);

            var enabledModules = moduleOptions?.EnabledModules ?? Array.Empty<string>();
            var enabledSet = enabledModules.Length > 0
                ? new HashSet<string>(enabledModules.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase)
                : null;

            var resolvedContentRoot = string.IsNullOrWhiteSpace(contentRoot)
                ? ResolveContentRoot()
                : contentRoot;
            var resolvedModulesRoot = string.IsNullOrWhiteSpace(modulesRoot)
                ? Path.Combine(resolvedContentRoot, "Modules")
                : modulesRoot;

            ValLog.Info("ModuleLoader", $"Resolved ModulesRoot: {resolvedModulesRoot}");

            // MAIN layout:
            //  - Dock\Dock.module.json  (core UI)
            //  - Modules\**\*.module.json (feature modules)
            var rootsToScan = new List<string>
            {
                Path.Combine(resolvedContentRoot, "Dock"),
                resolvedModulesRoot
            };

            async Task LoadModuleConfig(CoreWebView2 coreWebView2, string moduleDir, string configPath, string moduleNameFromFile)
            {
                if (string.IsNullOrWhiteSpace(moduleDir) || string.IsNullOrWhiteSpace(configPath))
                    return;

                if (!File.Exists(configPath))
                    return;

                ModuleManifest? manifest;
                try
                {
                    var jsonCfg = File.ReadAllText(configPath);
                    manifest = JsonSerializer.Deserialize<ModuleManifest>(
                        jsonCfg,
                        ValJsonOptions.CaseInsensitive);
                }
                catch (Exception ex)
                {
                    var details = FormatConfigExceptionDetails(ex);
                    var reason = $"Config parse error: {details}";
                    ValLog.Warn("ModuleLoader", $"Skipping module in '{moduleDir}': {reason}");
                    RecordModuleStatus(configPath, moduleNameFromFile, $"Skipped ({reason})");
                    return;
                }

                if (manifest == null)
                {
                    var reason = "Invalid manifest: Empty or unreadable config.";
                    ValLog.Warn("ModuleLoader", $"Skipping module in '{moduleDir}': {reason}");
                    RecordModuleStatus(configPath, moduleNameFromFile, $"Skipped ({reason})");
                    return;
                }

                var moduleName = !string.IsNullOrWhiteSpace(manifest.name)
                    ? manifest.name!.Trim()
                    : moduleNameFromFile;

                if (string.IsNullOrWhiteSpace(moduleName))
                {
                    var reason = "Invalid manifest: name is required.";
                    ValLog.Warn("ModuleLoader", $"Skipping module in '{moduleDir}': {reason}");
                    RecordModuleStatus(configPath, moduleNameFromFile, $"Skipped ({reason})");
                    return;
                }

                if (string.IsNullOrWhiteSpace(manifest.version))
                {
                    var reason = "Invalid manifest: version is required.";
                    ValLog.Warn("ModuleLoader", $"Skipping module in '{moduleDir}': {reason}");
                    RecordModuleStatus(configPath, moduleName, $"Skipped ({reason})");
                    return;
                }

                if (string.IsNullOrWhiteSpace(manifest.apiVersion))
                {
                    var reason = "Invalid manifest: apiVersion is required.";
                    ValLog.Warn("ModuleLoader", $"Skipping module in '{moduleDir}': {reason}");
                    RecordModuleStatus(configPath, moduleName, $"Skipped ({reason})");
                    return;
                }

                if (!string.Equals(manifest.apiVersion, SupportedApiVersion, StringComparison.OrdinalIgnoreCase))
                {
                    var reason = $"Incompatible apiVersion '{manifest.apiVersion}'. Host supports '{SupportedApiVersion}'.";
                    ValLog.Warn("ModuleLoader", $"Skipping module in '{moduleDir}': {reason}");
                    RecordModuleStatus(configPath, moduleName, $"Skipped ({reason})");
                    return;
                }

                var hostMinVersionRaw = manifest.hostMinVersion ?? manifest.minHostVersion;
                if (string.IsNullOrWhiteSpace(hostMinVersionRaw))
                {
                    var reason = "Invalid manifest: hostMinVersion is required.";
                    ValLog.Warn("ModuleLoader", $"Skipping module in '{moduleDir}': {reason}");
                    RecordModuleStatus(configPath, moduleName, $"Skipped ({reason})");
                    return;
                }

                var hostMinVersionParsed = ParseVersion(hostMinVersionRaw);
                if (hostMinVersionParsed == null)
                {
                    var reason = $"Invalid manifest: hostMinVersion '{hostMinVersionRaw}' is not a valid version.";
                    ValLog.Warn("ModuleLoader", $"Skipping module in '{moduleDir}': {reason}");
                    RecordModuleStatus(configPath, moduleName, $"Skipped ({reason})");
                    return;
                }

                if (hostVersionParsed != null && hostVersionParsed < hostMinVersionParsed)
                {
                    var reason = $"Incompatible host version '{hostVersionParsed}'. Requires '{hostMinVersionParsed}'.";
                    ValLog.Warn("ModuleLoader", $"Skipping module in '{moduleDir}': {reason}");
                    RecordModuleStatus(configPath, moduleName, $"Skipped ({reason})");
                    return;
                }

                if (manifest.capabilities == null)
                {
                    var reason = "Invalid manifest: capabilities is required.";
                    ValLog.Warn("ModuleLoader", $"Skipping module in '{moduleDir}': {reason}");
                    RecordModuleStatus(configPath, moduleName, $"Skipped ({reason})");
                    return;
                }

                var invalidCapabilities = manifest.capabilities
                    .Where(capability => string.IsNullOrWhiteSpace(capability) || !SupportedCapabilities.Contains(capability.Trim()))
                    .Select(capability => string.IsNullOrWhiteSpace(capability) ? "<empty>" : capability.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (invalidCapabilities.Count > 0)
                {
                    var reason = $"Unsupported capabilities: {string.Join(", ", invalidCapabilities)}.";
                    ValLog.Warn("ModuleLoader", $"Skipping module in '{moduleDir}': {reason}");
                    RecordModuleStatus(configPath, moduleName, $"Skipped ({reason})");
                    return;
                }

                if (!manifest.enabled.HasValue)
                {
                    var reason = "Invalid manifest: enabled must be specified.";
                    ValLog.Warn("ModuleLoader", $"Skipping module in '{moduleDir}': {reason}");
                    RecordModuleStatus(configPath, moduleName, $"Skipped ({reason})");
                    return;
                }

                if (manifest.entryScripts == null || manifest.entryScripts.Length == 0)
                {
                    var reason = "Invalid manifest: entryScripts must include at least one script.";
                    ValLog.Warn("ModuleLoader", $"Skipping module in '{moduleDir}': {reason}");
                    RecordModuleStatus(configPath, moduleName, $"Skipped ({reason})");
                    return;
                }

                if (!manifest.enabled.Value)
                {
                    RecordModuleStatus(configPath, moduleName, "Disabled");
                    return;
                }

                if (enabledSet != null &&
                    !enabledSet.Contains(moduleName) &&
                    !enabledSet.Contains(moduleNameFromFile))
                {
                    RecordModuleStatus(configPath, moduleName, "Skipped (not enabled)");
                    return;
                }

                var scriptsToLoad = new List<string>();
                var seenScripts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var script in manifest.entryScripts)
                {
                    if (string.IsNullOrWhiteSpace(script))
                    {
                        var reason = "Invalid manifest: entryScripts contains an empty path.";
                        ValLog.Warn("ModuleLoader", $"Skipping module in '{moduleDir}': {reason}");
                        RecordModuleStatus(configPath, moduleName, $"Skipped ({reason})");
                        return;
                    }

                    var trimmed = script.Trim();
                    if (!seenScripts.Add(trimmed))
                        continue;

                    var scriptPath = Path.Combine(moduleDir, trimmed);
                    if (!File.Exists(scriptPath))
                    {
                        var reason = $"Invalid manifest: missing entry script '{trimmed}'.";
                        ValLog.Warn("ModuleLoader", $"Skipping module in '{moduleDir}': {reason}");
                        RecordModuleStatus(configPath, moduleName, $"Skipped ({reason})");
                        return;
                    }

                    scriptsToLoad.Add(trimmed);
                }

                var stylesToLoad = new List<string>();
                if (manifest.styles != null && manifest.styles.Length > 0)
                {
                    var seenStyles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var style in manifest.styles)
                    {
                        if (string.IsNullOrWhiteSpace(style))
                        {
                            var reason = "Invalid manifest: styles contains an empty path.";
                            ValLog.Warn("ModuleLoader", $"Skipping module in '{moduleDir}': {reason}");
                            RecordModuleStatus(configPath, moduleName, $"Skipped ({reason})");
                            return;
                        }

                        var trimmed = style.Trim();
                        if (!seenStyles.Add(trimmed))
                            continue;

                        var stylePath = Path.Combine(moduleDir, trimmed);
                        if (!File.Exists(stylePath))
                        {
                            var reason = $"Invalid manifest: missing style '{trimmed}'.";
                            ValLog.Warn("ModuleLoader", $"Skipping module in '{moduleDir}': {reason}");
                            RecordModuleStatus(configPath, moduleName, $"Skipped ({reason})");
                            return;
                        }

                        stylesToLoad.Add(trimmed);
                    }
                }

                // Load scripts in order (dedupe by relative path, case-insensitive).
                foreach (var rel in scriptsToLoad)
                {
                    if (string.IsNullOrWhiteSpace(rel)) continue;

                    var scriptPath = Path.Combine(moduleDir, rel);

                    try
                    {
                        var script = File.ReadAllText(scriptPath);
                        // Persist across navigations + run immediately for current document.
                        await coreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
                        await coreWebView2.ExecuteScriptAsync(script);
                    }
                    catch (Exception ex)
                    {
                        // Do not fail module load chain for one broken module.
                        ValLog.Warn("ModuleLoader", $"Script load failed for module '{moduleName}': {ex.GetType().Name}: {ex.Message}");
                    }
                }

                foreach (var rel in stylesToLoad)
                {
                    var stylePath = Path.Combine(moduleDir, rel);
                    try
                    {
                        var css = File.ReadAllText(stylePath);

                        // Important: document.head can be null at DocumentCreated time (especially during auth/login flows).
                        // Inject the style in a head-safe way so modules remain styled across navigations without requiring a relaunch.
                        var safeName = new string(moduleNameFromFile
                            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')
                            .ToArray());
                        var styleId = "val-style-" + safeName;

                        var js = "(() => { try {"
                               + "const id = " + JsonSerializer.Serialize(styleId) + ";"
                               + "const css = " + JsonSerializer.Serialize(css) + ";"
                               + "const add = () => { try {"
                               + "  if (document.getElementById(id)) return;"
                               + "  const s = document.createElement('style');"
                               + "  s.id = id;"
                               + "  s.textContent = css;"
                               + "  const t = document.head || document.documentElement || document.body;"
                               + "  if (t) t.appendChild(s);"
                               + "} catch {} };"
                               + "if (document.getElementById(id)) return;"
                               + "if (document.head || document.documentElement || document.body) { add(); }"
                               + "else if (document.readyState === 'loading') { document.addEventListener('DOMContentLoaded', add, { once: true }); }"
                               + "else { setTimeout(add, 0); }"
                               + "} catch {} })();";

                        await coreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(js);
                        await coreWebView2.ExecuteScriptAsync(js);
                    }
                    catch (Exception ex)
                    {
                        // ignore
                        ValLog.Warn("ModuleLoader", $"Style injection failed for module '{moduleName}': {ex.GetType().Name}: {ex.Message}");
                    }
                }

                RecordModuleStatus(configPath, moduleName, "Loaded");
            }

            try
            {
                // Discover configs from both roots (dedupe by full path).
                var configs = rootsToScan
                    .Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r))
                    .SelectMany(r => Directory.EnumerateFiles(r, "*.module.json", SearchOption.AllDirectories))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Quiet log to devtools (no toasts).
                try
                {
                    await core.ExecuteScriptAsync($"console.log('[VAL] ModuleLoader found {configs.Count} module config(s)')");
                }
                catch (Exception ex)
                {
                    if (RateLimiter.Allow("module.discovery.console_log", LogInterval))
                    {
                        ValLog.Warn(nameof(ModuleLoader),
                            $"Module discovery devtools log failed. {ex.GetType().Name}: {LogSanitizer.Sanitize(ex.Message)}");
                    }
                }

                foreach (var configPath in configs)
                {
                    try
                    {
                        if (!_registrationTracker.TryMarkRegistered(core, configPath))
                        {
                            ValLog.Verbose("ModuleLoader", $"Skipping already-registered module for this core: {configPath}");
                            continue;
                        }

                        var moduleDir = Path.GetDirectoryName(configPath);
                        if (string.IsNullOrWhiteSpace(moduleDir))
                            continue;

                        var fileName = Path.GetFileName(configPath);
                        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".module.json", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var moduleNameFromFile = fileName.Substring(0, fileName.Length - ".module.json".Length);
                        await LoadModuleConfig(core, moduleDir, configPath, moduleNameFromFile);
                    }
                    catch (Exception ex)
                    {
                        // keep loading remaining modules
                        ValLog.Warn("ModuleLoader", $"Module load failed: {configPath}. {ex.GetType().Name}: {ex.Message}");
                        ValLog.Error("ModuleLoader", ex.ToString());
                    }
                }
            }
            catch
            {
                // Enumeration failure should never prevent VAL from running.
                ValLog.Warn("ModuleLoader", "Module discovery failed.");
            }

            var statusSnapshot = GetModuleStatuses();
            var loadedCount = statusSnapshot.Count(status => string.Equals(status.Status, "Loaded", StringComparison.OrdinalIgnoreCase));
            var skippedCount = statusSnapshot.Count - loadedCount;
            ValLog.Info("ModuleLoader", $"Modules Status: {loadedCount} loaded, {skippedCount} skipped.");
        }
    }
}
