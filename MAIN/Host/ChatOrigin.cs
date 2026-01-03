namespace VAL.Host
{
    /// <summary>
    /// ChatOrigin: explicit genesis mode for a conversation.
    ///
    /// - Organic: the user initiated the chat normally (typed the first message manually).
    /// - ContinuumSeeded: the chat was created via Pulse / Essence injection.
    /// - ChronicleRebuilt: the chat's truth log was reconstructed from UI text by Chronicle.
    /// - Unknown: best-effort state before we can classify.
    /// </summary>
    public enum ChatOrigin
    {
        Unknown = 0,
        Organic = 1,
        ContinuumSeeded = 2,
        ChronicleRebuilt = 3,
    }
}
