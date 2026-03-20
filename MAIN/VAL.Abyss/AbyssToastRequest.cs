namespace VAL.Host.Abyss
{
    internal enum AbyssToastKind
    {
        NoQuery,
        Searching,
        NoTruthLogs,
        NoMatches,
        Matches,
        ResultsWritten,
        Injected,
        NoSelection,
        ActionUnavailable
    }

    internal readonly record struct AbyssToastRequest(
        AbyssToastKind Kind,
        string? ChatId = null,
        string? TitleOverride = null,
        bool BypassLaunchQuiet = false);
}
