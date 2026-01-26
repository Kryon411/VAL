using System;

namespace VAL.Host.WebMessaging
{
    internal readonly record struct WebMessageEnvelope(string Json, Uri SourceUri);
}
