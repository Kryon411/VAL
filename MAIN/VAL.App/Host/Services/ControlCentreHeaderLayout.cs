using System;
using System.Text.Json;
using System.Windows;

using VAL.Contracts;
using VAL.Host.Security;
using VAL.Host.WebMessaging;

namespace VAL.App.Host.Services
{
    internal static class ControlCentreHeaderLayout
    {
        public static bool TryRead(WebMessageEnvelope message, out Rect launcher)
        {
            launcher = Rect.Empty;
            if (!WebOriginPolicy.TryIsBridgeAllowed(message.SourceUri.AbsoluteUri, out _) ||
                !message.TryGetParsedEnvelope(out var envelope) ||
                envelope.Name != WebCommandNames.DockUiStateSet ||
                envelope.Type != WebMessageTypes.Command || envelope.Source != "dock" ||
                envelope.Payload is not { ValueKind: JsonValueKind.Object } payload ||
                !payload.TryGetProperty("headerAnchor", out var anchor) || anchor.ValueKind != JsonValueKind.Object)
                return false;

            if (!Read(anchor, "x", out var x) || !Read(anchor, "y", out var y) ||
                !Read(anchor, "w", out var w) || !Read(anchor, "h", out var h) ||
                !Read(anchor, "viewportWidth", out var viewportWidth) ||
                !Read(anchor, "viewportHeight", out var viewportHeight) ||
                viewportWidth is < 100 or > 20000 || viewportHeight is < 100 or > 20000 ||
                w is < 34 or > 64 || h is < 34 or > 64 || x < 0 || y < 0 ||
                x + w > viewportWidth || y + h > viewportHeight)
                return false;

            launcher = new Rect(x, y, w, h);
            return true;
        }

        private static bool Read(JsonElement root, string name, out double value)
        {
            value = 0;
            return root.TryGetProperty(name, out var property) &&
                property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out value) && double.IsFinite(value);
        }
    }
}
