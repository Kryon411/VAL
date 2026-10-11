using System;
using System.IO;
using System.Linq;
using System.Threading;

using VAL.App.State;

namespace VAL.App.Host.Services;

public sealed class DataWipeService : IDataWipeService
{
    private readonly IAppPaths _paths;
    private readonly ILog _log;
    private readonly IWebViewRuntime? _webView;
    internal string PendingPath => Path.Combine(_paths.StateRoot, "pending-data-wipe.json");

    public DataWipeService(IAppPaths paths, ILog log, IWebViewRuntime? webView = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _webView = webView;
    }

    public void ScheduleWipe()
    {
        var targets = GetTargets();
        if (!IsSafeTarget(_paths.StateRoot, allowState: true) || targets.Any(target => !IsSafeTarget(target)))
            throw new InvalidOperationException("A configured cleanup folder is outside VAL's managed data tree or is a link.");

        // Persist before closing. Live services and browser processes are never deleted underneath.
        JsonStateFile.Write(PendingPath, new PendingWipe(1, Path.GetFullPath(_paths.DataRoot), targets,
            BrowserProcessIdentity.Capture(_webView)));
    }

    public DataWipeResult? CompletePendingWipe()
    {
        var state = JsonStateFile.Read<PendingWipe>(PendingPath);
        if (state.Status == JsonStateFileReadStatus.Missing) return null;
        if (!IsSafeTarget(_paths.StateRoot, allowState: true))
            return new DataWipeResult(false, false, 0, 1);

        var targets = GetTargets();
        try
        {
            if (!state.IsSuccess || state.Value!.Version != 1 ||
                !SamePath(state.Value.DataRoot, _paths.DataRoot) ||
                state.Value.Targets == null || !targets.SequenceEqual(state.Value.Targets, StringComparer.OrdinalIgnoreCase) ||
                targets.Any(target => !IsSafeTarget(target)))
                return new DataWipeResult(false, false, 0, Math.Max(1, targets.Length));
        }
        catch
        {
            return new DataWipeResult(false, false, 0, Math.Max(1, targets.Length));
        }

        int deleted = 0;
        int failed = 0;
        if (state.Value!.Browser is { } browser && !browser.WaitForExit(TimeSpan.FromSeconds(5)))
            return new DataWipeResult(false, false, 0, targets.Length);
        // Read settings without constructing the live Continuum/Portal graph.
        new PrivacySettingsService(_paths, _ => { }, _log).DisableCapture();
        foreach (var target in targets)
        {
            if (TryDeleteDirectory(target)) deleted++;
            else failed++;
        }

        // Retain failures so the next launch retries before any capture can start.
        if (failed == 0)
        {
            // Allow browser-child teardown to settle, then verify every target together.
            Thread.Sleep(100);
            foreach (var target in targets)
                if (!TryDeleteDirectory(target)) failed++;
            if (failed > 0) return new DataWipeResult(false, true, deleted, failed);
            File.Delete(PendingPath);
            if (File.Exists(PendingPath)) return new DataWipeResult(false, true, deleted, 1);
        }
        return new DataWipeResult(failed == 0, failed > 0 && deleted > 0, deleted, failed);
    }

    private string[] GetTargets() => new[]
    {
        _paths.LogsRoot,
        _paths.ProfileRoot,
        Path.Combine(_paths.DataRoot, "Snapshots"),
        Path.Combine(_paths.DataRoot, "Staging"),
        _paths.MemoryChatsRoot,
    }.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private bool TryDeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (!IsSafeTarget(path)) return false;
            try
            {
                if (Directory.Exists(path))
                {
                    // Reject nested junctions rather than trusting recursive traversal.
                    if (HasLinks(path)) return false;
                    Directory.Delete(path, recursive: true);
                }
                return !Directory.Exists(path) && !File.Exists(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (attempt < 9) Thread.Sleep(100);
        }
        return false;
    }

    private static bool HasLinks(string directory)
    {
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            if (entry is DirectoryInfo child && HasLinks(child.FullName)) return true;
        }
        return false;
    }

    private bool IsSafeTarget(string path, bool allowState = false)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_paths.DataRoot));
            var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (SamePath(root, Path.GetPathRoot(root)!) ||
                !target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;

            foreach (var protectedPath in new[] { _paths.ProductRoot, _paths.ContentRoot, _paths.StateRoot })
            {
                var protectedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(protectedPath));
                if (allowState && SamePath(protectedRoot, _paths.StateRoot)) continue;
                if (SamePath(target, protectedRoot) ||
                    protectedRoot.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    target.StartsWith(protectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            for (var directory = new DirectoryInfo(target); directory != null; directory = directory.Parent)
            {
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            }
            return true;
        }
        catch { return false; }
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);

    private sealed record PendingWipe(int Version, string DataRoot, string[] Targets, BrowserProcessIdentity? Browser = null);
}
