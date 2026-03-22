using System;

namespace VAL.Host.WebMessaging
{
    public readonly record struct WebMessageEnvelope
    {
        public string Json { get; }
        public Uri SourceUri { get; }
        public MessageEnvelope? ParsedEnvelope { get; }

        public WebMessageEnvelope(string json, Uri sourceUri)
            : this(json, sourceUri, parsedEnvelope: null)
        {
        }

        public WebMessageEnvelope(string json, Uri sourceUri, MessageEnvelope? parsedEnvelope)
        {
            Json = json ?? string.Empty;
            SourceUri = sourceUri ?? throw new ArgumentNullException(nameof(sourceUri));
            ParsedEnvelope = parsedEnvelope;
        }

        public bool TryGetParsedEnvelope(out MessageEnvelope envelope)
        {
            if (ParsedEnvelope != null)
            {
                envelope = ParsedEnvelope;
                return true;
            }

            return MessageEnvelope.TryParse(Json, out envelope);
        }
    }
}
