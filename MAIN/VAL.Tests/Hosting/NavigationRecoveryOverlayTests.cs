using System.Windows;

using VAL.App.Host.Services;

using Xunit;

namespace VAL.Tests.Hosting;

public sealed class NavigationRecoveryOverlayTests
{
    [Theory]
    [InlineData(0, 0, 1280, 720, 140, 32, 12, 12)]
    [InlineData(-1920, 100, 1280, 720, 140, 32, -1908, 112)]
    [InlineData(100, 200, 160, 56, 140, 32, 108, 212)]
    [InlineData(100, 200, 80, 20, 140, 32, 100, 200)]
    public void PillPlacementUsesOnlyTheBrowserViewportAndStaysAtItsNearestValidInset(
        double x, double y, double width, double height, double buttonWidth, double buttonHeight,
        double expectedX, double expectedY)
    {
        var position = NavigationRecoveryOverlayHost.CalculatePosition(
            new Rect(x, y, width, height), new Size(buttonWidth, buttonHeight));

        Assert.Equal(new Point(expectedX, expectedY), position);
    }
}
