using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

using VAL.App.Host.Services;
using VAL.Contracts;
using VAL.Host;
using VAL.Host.Services;
using VAL.Host.WebMessaging;

namespace VAL.Tests.Shared;

internal sealed class TestWebViewRuntime : IWebViewRuntime
{
    public bool IsReady { get; set; }
    public bool IsBridgeReady { get; set; }
    public long NavigationGeneration => 0;
    public CoreWebView2? Core => null;
    public Uri? LastChatUri { get; set; }
    public List<string> NavigationRequests { get; } = [];
    public Exception? NavigationFailure { get; set; }
    public int ScriptCalls { get; private set; }
    public int PostedMessages { get; private set; }
    public event Action<WebMessageEnvelope>? WebMessageJsonReceived { add { } remove { } }
    public event Action? NavigationCompleted { add { } remove { } }
    public event Action? NavigationStateChanged { add { } remove { } }
    public Task InitializeAsync(WebView2 control) => Task.CompletedTask;
    public void PostJson(string json) => PostedMessages++;
    public Task ExecuteScriptAsync(string js) { ScriptCalls++; return Task.CompletedTask; }
    public Task<string?> EvaluateScriptAsync(string js) { ScriptCalls++; return Task.FromResult<string?>(null); }
    public void Navigate(string url)
    {
        if (NavigationFailure != null) throw NavigationFailure;
        NavigationRequests.Add(url);
    }
    public bool TryGoBack() => false;
}

internal sealed class RecordingDesktopDialogs : IDesktopDialogService
{
    public List<(string Message, string Caption)> Errors { get; } = [];
    public bool ConfirmWarning(string message, string caption) => false;
    public void ShowError(string message, string caption) => Errors.Add((message, caption));
}

internal sealed class RecordingRuntimeSender : IWebMessageSender
{
    public ConcurrentQueue<MessageEnvelope> Messages { get; } = new();
    public void Send(MessageEnvelope envelope) => Messages.Enqueue(envelope);
}

internal sealed class NullToastHub : IToastHub
{
    public bool IsLaunchQuietPeriodActive => false;
    public bool TryShow(ToastKey key, string? chatId = null, bool bypassLaunchQuiet = false,
        string? titleOverride = null, string? subtitleOverride = null, string? groupKeyOverride = null,
        bool? replaceGroupOverride = null, bool? bypassBurstDedupeOverride = null,
        bool? oncePerChatOverride = null, string? ledgerIdOverride = null,
        ToastOrigin origin = ToastOrigin.Unknown, ToastReason reason = ToastReason.Unknown) => true;
    public bool TryShowActions(ToastKey key, (string Label, Action OnClick)[] actions,
        string? chatId = null, bool bypassLaunchQuiet = false, string? titleOverride = null,
        string? subtitleOverride = null, string? groupKeyOverride = null, bool? replaceGroupOverride = null,
        bool? oncePerChatOverride = null, string? ledgerIdOverride = null,
        ToastOrigin origin = ToastOrigin.Unknown, ToastReason reason = ToastReason.Unknown) => true;
    public void TryShowOperationCancelled(string groupKey, ToastOrigin origin = ToastOrigin.Unknown,
        ToastReason reason = ToastReason.Unknown)
    { }
    public void DismissGroup(string groupKey) { }
}

internal sealed class NullProcessLauncher : IProcessLauncher
{
    public void OpenPath(string path) { }
    public void OpenFolder(string path) { }
    public void OpenUrl(string url) { }
}

internal sealed class QueuedTestUiThread : IUiThread
{
    private readonly ConcurrentQueue<(Action Action, TaskCompletionSource Completion)> _queue = new();
    public void Invoke(Action action) => action();
    public Task InvokeAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue((action, completion));
        return completion.Task;
    }
    public async Task<Action> TakeNextAsync()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (_queue.TryDequeue(out var work))
                return () =>
                {
                    try { work.Action(); work.Completion.SetResult(); }
                    catch (Exception ex) { work.Completion.SetException(ex); throw; }
                };
            await Task.Delay(10);
        }
        throw new TimeoutException("No UI publication arrived.");
    }
    public IDisposable StartTimer(TimeSpan interval, Action tick) => new NoopDisposable();
    private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
}
