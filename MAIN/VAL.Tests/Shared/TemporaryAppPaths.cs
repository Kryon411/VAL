using System;
using System.IO;

using VAL.Host;
using VAL.Host.Services;

namespace VAL.Tests.Shared;

internal sealed class TemporaryAppPaths : IAppPaths, IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "VAL.Tests", Guid.NewGuid().ToString("N"));
    public string DataRoot { get; }
    public string ProductRoot { get; set; }
    public string ContentRoot => ProductRoot;
    public string StateRoot => Path.Combine(DataRoot, "State");
    public string LogsRoot { get; set; }
    public string ProfileRoot { get; set; }
    public string ModulesRoot => Path.Combine(ProductRoot, "Modules");
    public string MemoryChatsRoot => Path.Combine(DataRoot, "Memory", "Chats");

    public TemporaryAppPaths()
    {
        DataRoot = Path.Combine(Root, "Data");
        ProductRoot = Path.Combine(Root, "Program");
        LogsRoot = Path.Combine(DataRoot, "Logs");
        ProfileRoot = Path.Combine(DataRoot, "Profile");
        Directory.CreateDirectory(DataRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}

internal sealed class NullTestLog : ILog
{
    public void Info(string category, string message) { }
    public void Warn(string category, string message) { }
    public void LogError(string category, string message) { }
    public void Verbose(string category, string message) { }
}

internal sealed class TestPortalStateManager : IPortalRuntimeStateManager
{
    public bool Cleared { get; private set; }
    public void SetEnabled(bool enabled) { }
    public void SetPrivacyAllowed(bool allowed) { }
    public void ClearStaging() => Cleared = true;
    public void OpenSnipOverlay() { }
    public void SendStaged(int max) { }
}
