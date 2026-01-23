using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using VAL.Host.Services;
using VAL.Host;

namespace VAL.Host.WebMessaging
{
    public sealed class WebMessageSender : IWebMessageSender
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly IWebViewRuntime _webViewRuntime;

        public WebMessageSender(IWebViewRuntime webViewRuntime)
        {
            _webViewRuntime = webViewRuntime;
        }

        public void Send(MessageEnvelope envelope)
        {
            if (envelope == null)
                return;

            try
            {
                var json = JsonSerializer.Serialize(envelope, Options);
                _webViewRuntime.PostJson(json);
            }
            catch (Exception)
            {
                ValLog.Warn(nameof(WebMessageSender), "Failed to send web message envelope.");
            }
        }
    }
}
