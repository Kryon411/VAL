using System.Windows;

using VAL.App.Host.Services;
using VAL.App.State;

using Xunit;

namespace VAL.Tests.Hosting
{
    public sealed class ControlCentreOverlayHostTests
    {
        [Fact]
        public void ClampGeometryConstrainsSizeAndPositionToVirtualScreen()
        {
            var geometry = new GeometryState(x: 800, y: 700, w: 120, h: 120);
            var bounds = new Rect(0, 0, 640, 480);

            var clamped = ControlCentreOverlayHost.ClampGeometry(
                geometry,
                bounds,
                minWidth: 34,
                minHeight: 34,
                maxWidth: 64,
                maxHeight: 64);

            Assert.Equal(64, clamped.W);
            Assert.Equal(64, clamped.H);
            Assert.Equal(576, clamped.X);
            Assert.Equal(416, clamped.Y);
        }

        [Fact]
        public void ClampGeometryUsesCurrentSizeWhenMaximumIsUnbounded()
        {
            var geometry = new GeometryState(x: -30, y: -10, w: 50, h: 48);
            var bounds = new Rect(0, 0, 400, 300);

            var clamped = ControlCentreOverlayHost.ClampGeometry(
                geometry,
                bounds,
                minWidth: 34,
                minHeight: 34,
                maxWidth: double.PositiveInfinity,
                maxHeight: double.PositiveInfinity);

            Assert.Equal(50, clamped.W);
            Assert.Equal(48, clamped.H);
            Assert.Equal(0, clamped.X);
            Assert.Equal(0, clamped.Y);
        }
    }
}
