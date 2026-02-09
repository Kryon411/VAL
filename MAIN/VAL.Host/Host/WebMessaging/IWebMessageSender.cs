namespace VAL.Host.WebMessaging
{
    public interface IWebMessageSender
    {
        void Send(MessageEnvelope envelope);
    }
}
