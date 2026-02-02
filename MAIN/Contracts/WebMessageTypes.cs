namespace VAL.Contracts
{
    /// <summary>
    /// Message type discriminator values for web messaging envelopes.
    /// </summary>
    /// <remarks>
    /// Commands request the host to perform an action. Events are notifications that
    /// describe something that already happened and are safe to ignore if unhandled.
    /// Logs are diagnostic-only messages.
    /// </remarks>
    public static class WebMessageTypes
    {
        public const string Command = "command";
        public const string Event = "event";
        public const string Log = "log";
    }
}
