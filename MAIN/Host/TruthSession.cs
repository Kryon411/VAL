namespace VAL.Host
{
    public static class TruthSession
    {
        public static string CurrentChatId => SessionContext.ActiveChatId;
    }
}
