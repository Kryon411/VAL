using System.Windows;

using VAL.App.Host.Services;

using Xunit;

namespace VAL.Tests.Hosting
{
    public sealed class ControlCentreOverlayPlacementTests
    {
        [Fact]
        public void CreateDefaultAnchorsToTopRightOfWebViewInDipSpace()
        {
            var geometry = ControlCentreOverlayPlacement.CreateDefault(
                webViewOriginPx: new Point(200, 120),
                webViewWidthDip: 500,
                dpiScaleX: 1.0,
                dpiScaleY: 1.0);

            Assert.Equal(644, geometry.X);
            Assert.Equal(132, geometry.Y);
            Assert.Equal(40, geometry.W);
            Assert.Equal(40, geometry.H);
        }

        [Fact]
        public void CreateDefaultConvertsScreenPixelsUsingDpiScale()
        {
            var geometry = ControlCentreOverlayPlacement.CreateDefault(
                webViewOriginPx: new Point(300, 225),
                webViewWidthDip: 500,
                dpiScaleX: 1.5,
                dpiScaleY: 1.25);

            Assert.Equal(644, geometry.X);
            Assert.Equal(192, geometry.Y);
            Assert.Equal(40, geometry.W);
            Assert.Equal(40, geometry.H);
        }
    }
}
