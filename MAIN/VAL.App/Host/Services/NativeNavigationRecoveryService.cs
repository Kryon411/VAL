using System;

using VAL.Host;
using VAL.Host.Services;

namespace VAL.App.Host.Services;

public sealed class NativeNavigationRecoveryService
{
    internal const string ChatHomeUrl = "https://chatgpt.com/";
    private readonly IWebViewRuntime _runtime;
    private readonly IOperationCoordinator _operations;
    private readonly IDesktopDialogService _dialogs;
    private readonly ILog _log;

    public NativeNavigationRecoveryService(
        IWebViewRuntime runtime,
        IOperationCoordinator operations,
        IDesktopDialogService dialogs,
        ILog log)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public bool IsAvailable => _runtime.IsReady && !_runtime.IsBridgeReady;

    public bool TryReturnToChatGpt()
    {
        if (!IsAvailable)
            return false;

        if (_operations.IsBusy)
        {
            _dialogs.ShowError("Finish or cancel the current operation before returning to ChatGPT.", "VAL");
            return false;
        }

        try
        {
            // Native navigation must not depend on page scripts or a remembered authentication URL.
            _runtime.Navigate(ChatHomeUrl);
            return true;
        }
        catch (Exception ex)
        {
            _log.Warn(nameof(NativeNavigationRecoveryService), $"Native return navigation failed ({ex.GetType().Name}).");
            _dialogs.ShowError("VAL could not return to ChatGPT. Please try again or restart the app.", "VAL");
            return false;
        }
    }
}
