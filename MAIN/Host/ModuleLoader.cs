using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

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
        // Idempotency guard: prevent duplicate module injection (especially across navigations).
        private static readonly HashSet<string> _loadedConfigPaths = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _loadLock = new();

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

        private static string ResolveAppRoot()
        {
            // Stable root regardless of WorkingDirectory.
            // Priority:
            // 1) A directory (or parent) that contains "Modules"
            // 2) A sibling "PRODUCT" directory that contains "Modules"
            // 3) CurrentDirectory (or parent) that contains "Modules"
            // 4) AppContext.BaseDirectory
            string exeDir = AppContext.BaseDirectory;
            string cwd = Directory.GetCurrentDirectory();

            static string FindWithModules(string startDir, int maxUp)
            {
                try
                {
                    string dir = startDir;
                    for (int i = 0; i <= maxUp; i++)
                    {
                        if (Directory.Exists(Path.Combine(dir, "Modules")))
                            return dir;

                        var parent = Directory.GetParent(dir);
                        if (parent == null) break;
                        dir = parent.FullName;
                    }
                }
                catch { }

                return startDir;
            }

            // 1) exeDir or parents
            try
            {
                var fromExe = FindWithModules(exeDir, 6);
                if (Directory.Exists(Path.Combine(fromExe, "Modules")))
                    return fromExe;
            }
            catch { }

            // 2) look for sibling PRODUCT
            try
            {
                string dir = exeDir;
                for (int i = 0; i < 10; i++)
                {
                    var parent = Directory.GetParent(dir);
                    if (parent == null) break;
                    dir = parent.FullName;

                    var candidate = Path.Combine(dir, "PRODUCT");
                    if (Directory.Exists(Path.Combine(candidate, "Modules")))
                        return candidate;
                }
            }
            catch { }

            // 3) cwd or parents
            try
            {
                var fromCwd = FindWithModules(cwd, 6);
                if (Directory.Exists(Path.Combine(fromCwd, "Modules")))
                    return fromCwd;
            }
            catch { }

            return exeDir;
        }

        public static async Task Initialize(CoreWebView2 core)
        {
            if (core == null)
                return;

            string baseDir = ResolveAppRoot();

            // MAIN layout:
            //  - Dock\Dock.module.json  (core UI)
            //  - Modules\**\*.module.json (feature modules)
            var rootsToScan = new List<string>
            {
                Path.Combine(baseDir, "Dock"),
                Path.Combine(baseDir, "Modules")
            };

            static async Task LoadModuleConfig(CoreWebView2 coreWebView2, string moduleDir, string configPath, string moduleNameFromFile)
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
                catch
                {
                    ValLog.Warn("ModuleLoader", $"Failed to parse module config: {configPath}");
                    return;
                }

                if (cfg == null || !cfg.enabled)
                    return;

                var moduleName = !string.IsNullOrWhiteSpace(cfg.name)
                    ? cfg.name!.Trim()
                    : moduleNameFromFile;

                if (string.IsNullOrWhiteSpace(moduleName))
                    return;

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
                    catch
                    {
                        // Do not fail module load chain for one broken module.
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
                    catch
                    {
                        // ignore
                    }
                }
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
                        lock (_loadLock)
                        {
                            if (_loadedConfigPaths.Contains(configPath)) continue;
                            _loadedConfigPaths.Add(configPath);
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
                    catch
                    {
                        // keep loading remaining modules
                        ValLog.Warn("ModuleLoader", $"Module load failed: {configPath}");
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
