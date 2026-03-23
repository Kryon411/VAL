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
        private readonly ILog _log;

        public WebMessageSender(IWebViewRuntime webViewRuntime, ILog log)
        {
            _webViewRuntime = webViewRuntime ?? throw new ArgumentNullException(nameof(webViewRuntime));
            _log = log ?? throw new ArgumentNullException(nameof(log));
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
                _log.Warn(nameof(WebMessageSender), "Failed to send web message envelope.");
            }
        }
    }
}
