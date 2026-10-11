using System;

namespace VAL.Host.Services;

public sealed class ValidationSessionSettings
{
    private ValidationSessionSettings(bool enabled, string? dataRoot)
    {
        Enabled = enabled;
        DataRoot = dataRoot;
    }

    public bool Enabled { get; }
    public string? DataRoot { get; }

    public static ValidationSessionSettings FromArgs(string[]? args)
    {
        var enabled = false;
        string? dataRoot = null;
        args ??= Array.Empty<string>();
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index] ?? string.Empty;
            if (argument.Equals("--validation-session", StringComparison.OrdinalIgnoreCase)) enabled = true;
            else if (argument.StartsWith("--validation-data-root=", StringComparison.OrdinalIgnoreCase))
                dataRoot = argument["--validation-data-root=".Length..];
            else if (argument.Equals("--validation-data-root", StringComparison.OrdinalIgnoreCase))
            {
                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("A validation data root requires a path.");
                dataRoot = args[index];
            }
        }
        if (dataRoot != null && (!enabled || string.IsNullOrWhiteSpace(dataRoot)))
            throw new ArgumentException("A nonempty validation data root requires --validation-session.");
        return new ValidationSessionSettings(enabled, dataRoot);
    }
}
