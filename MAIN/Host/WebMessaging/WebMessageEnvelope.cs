using System;

namespace VAL.Host.WebMessaging
{
    public readonly record struct WebMessageEnvelope(string Json, Uri SourceUri);
}
