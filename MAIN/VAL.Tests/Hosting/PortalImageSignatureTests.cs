using System.Windows.Media;
using System.Windows.Media.Imaging;

using VAL.Host.Portal;

using Xunit;

namespace VAL.Tests.Hosting;

public sealed class PortalImageSignatureTests
{
    [Fact]
    public void ChangedInteriorPixelsAreNotMistakenForADuplicateScreenshot()
    {
        const int width = 32;
        const int height = 32;
        var pixels = new byte[width * height * 4];
        var first = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        first.Freeze();
        pixels[(16 * width + 16) * 4] = 255;
        var second = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        second.Freeze();
        Assert.NotEqual(PortalRuntime.ComputeSignature(first), PortalRuntime.ComputeSignature(second));
    }

    [Theory]
    [InlineData(32)]
    [InlineData(1920)]
    [InlineData(3840)]
    public void SignatureSupportsCommonScreenshotWidths(int width)
    {
        const int height = 20;
        var pixels = new byte[width * height * 4];
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze();
        var signature = PortalRuntime.ComputeSignature(image);
        Assert.NotEmpty(signature);
        Assert.Equal(signature, PortalRuntime.ComputeSignature(image));
    }
}
