using System;
using System.Windows;

using VAL.App.State;

namespace VAL.App.Host.Services
{
    public static class ControlCentreOverlayPlacement
    {
        public static GeometryState CreateDefault(
            Point webViewOriginPx,
            double webViewWidthDip,
            double dpiScaleX,
            double dpiScaleY,
            double width = 40d,
            double height = 40d,
            double rightInset = 16d,
            double topInset = 12d)
        {
            var webViewOriginDip = new Point(
                webViewOriginPx.X / Math.Max(dpiScaleX, double.Epsilon),
                webViewOriginPx.Y / Math.Max(dpiScaleY, double.Epsilon));
            var x = webViewOriginDip.X + Math.Max(0, webViewWidthDip - width - rightInset);
            var y = webViewOriginDip.Y + topInset;
            return new GeometryState(x, y, width, height);
        }
    }
}
