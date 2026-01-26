using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
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
        private static readonly ModuleRegistrationTracker _registrationTracker = new();
        private static readonly object _statusLock = new();
        private static readonly Dictionary<string, ModuleStatusInfo> _moduleStatuses = new(StringComparer.OrdinalIgnoreCase);

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

        private sealed class ModuleConfig
        {
            public string? name { get; set; }
            public string? description { get; set; }
            public bool enabled { get; set; } = true;

            // Paths are interpreted as relative to the directory containing the *.module.json file.
            public string? entry { get; set; }
            public string? styles { get; set; }

            public string? version { get; set; }

            // Optional: additional scripts to load (in order). If present, these are loaded.
            public string[]? scripts { get; set; }
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
                    var line = jsonException.LineNumber?.ToString() ?? "?";
                    var position = jsonException.BytePositionInLine?.ToString() ?? "?";
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

        public static async Task Initialize(CoreWebView2 core, string? modulesRoot, string? contentRoot, ModuleOptions? moduleOptions = null)
        {
            if (core == null)
                return;

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

                ModuleConfig? cfg;
                try
                {
                    var jsonCfg = File.ReadAllText(configPath);
                    cfg = JsonSerializer.Deserialize<ModuleConfig>(
                        jsonCfg,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (Exception ex)
                {
                    var details = FormatConfigExceptionDetails(ex);
                    ValLog.Warn("ModuleLoader", $"Failed to parse module config: {configPath}. {details} Module disabled due to config error.");
                    RecordModuleStatus(configPath, moduleNameFromFile, "Disabled (config parse error)");
                    return;
                }

                if (cfg == null || !cfg.enabled)
                    return;

                var moduleName = !string.IsNullOrWhiteSpace(cfg.name)
                    ? cfg.name!.Trim()
                    : moduleNameFromFile;

                if (string.IsNullOrWhiteSpace(moduleName))
                    return;

                if (enabledSet != null &&
                    !enabledSet.Contains(moduleName) &&
                    !enabledSet.Contains(moduleNameFromFile))
                {
                    return;
                }

                // Determine entry + scripts
                var entryRel = string.IsNullOrWhiteSpace(cfg.entry)
                    ? (moduleNameFromFile + ".main.js")
                    : cfg.entry!.Trim();

                var scriptsToLoad = new List<string>();

                if (cfg.scripts != null && cfg.scripts.Length > 0)
                {
                    foreach (var s in cfg.scripts)
                    {
                        if (string.IsNullOrWhiteSpace(s)) continue;
                        scriptsToLoad.Add(s.Trim());
                    }
                }

                // Ensure entry is included (and loaded first) unless scripts already cover it.
                if (!string.IsNullOrWhiteSpace(entryRel) &&
                    !scriptsToLoad.Any(s => string.Equals(s, entryRel, StringComparison.OrdinalIgnoreCase)))
                {
                    scriptsToLoad.Insert(0, entryRel);
                }

                if (scriptsToLoad.Count == 0 && !string.IsNullOrWhiteSpace(entryRel))
                    scriptsToLoad.Add(entryRel);

                // Load scripts in order (dedupe by relative path, case-insensitive).
                var seenScripts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var rel in scriptsToLoad)
                {
                    if (string.IsNullOrWhiteSpace(rel)) continue;
                    if (!seenScripts.Add(rel)) continue;

                    var scriptPath = Path.Combine(moduleDir, rel);
                    if (!File.Exists(scriptPath))
                        continue;

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

                // Styles
                string? stylePath = null;
                if (!string.IsNullOrWhiteSpace(cfg.styles))
                {
                    stylePath = Path.Combine(moduleDir, cfg.styles!.Trim());
                }
                else
                {
                    var conventional = Path.Combine(moduleDir, moduleNameFromFile + ".styles.css");
                    if (File.Exists(conventional))
                        stylePath = conventional;
                }

                if (!string.IsNullOrWhiteSpace(stylePath) && File.Exists(stylePath))
                {
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
                catch { }

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
        }
    }
}
