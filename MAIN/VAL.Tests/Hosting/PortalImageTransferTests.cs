using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using VAL.Host.Portal;

using Xunit;

namespace VAL.Tests.Hosting;

public sealed class PortalImageTransferTests
{
    [Fact]
    public async Task MissingForegroundPermissionDoesNotFocusOrTouchClipboard()
    {
        var fixture = new Fixture { Allowed = false };
        Assert.Equal(0, await fixture.RunAsync());
        Assert.Equal(0, fixture.FocusCalls);
        Assert.Empty(fixture.ClipboardImages);
        Assert.Equal(2, fixture.Staging.Count);
    }

    [Fact]
    public async Task FailedInitialFocusDoesNotDrainStaging()
    {
        var fixture = new Fixture { Focus = _ => Task.FromResult(false) };
        Assert.Equal(0, await fixture.RunAsync());
        Assert.Empty(fixture.ClipboardImages);
        Assert.Equal(2, fixture.Staging.Count);
    }

    [Fact]
    public async Task FailedSecondFocusRestoresEveryUnpastedImageInOrder()
    {
        var fixture = new Fixture { Focus = call => Task.FromResult(call == 1) };
        Assert.Equal(0, await fixture.RunAsync());
        Assert.Single(fixture.ClipboardImages);
        Assert.Equal(0, fixture.PasteCalls);
        Assert.Equal(fixture.Images, fixture.Staging.Drain(10));
    }

    [Fact]
    public async Task NavigationDuringFocusAbortsBeforeDraining()
    {
        var fixture = new Fixture();
        fixture.Focus = _ =>
        {
            fixture.Allowed = false;
            return Task.FromResult(true);
        };
        Assert.Equal(0, await fixture.RunAsync());
        Assert.Empty(fixture.ClipboardImages);
        Assert.Equal(2, fixture.Staging.Count);
    }

    [Fact]
    public async Task NavigationDuringClipboardWaitAbortsAndRestoresImages()
    {
        var fixture = new Fixture();
        fixture.Delay = _ =>
        {
            fixture.Allowed = false;
            return Task.CompletedTask;
        };
        Assert.Equal(0, await fixture.RunAsync());
        Assert.Equal(0, fixture.PasteCalls);
        Assert.Equal(fixture.Images, fixture.Staging.Drain(10));
    }

    [Fact]
    public async Task ExplicitPrivacyDisableOrClearDoesNotResurrectDrainedImages()
    {
        var fixture = new Fixture();
        fixture.Delay = _ =>
        {
            fixture.Allowed = false;
            fixture.RestoreAllowed = false;
            fixture.Staging.Clear();
            return Task.CompletedTask;
        };
        Assert.Equal(0, await fixture.RunAsync());
        Assert.Equal(0, fixture.PasteCalls);
        Assert.Equal(0, fixture.Staging.Count);
    }

    [Fact]
    public async Task FailedClipboardWriteRestoresUnpastedImages()
    {
        var fixture = new Fixture { SetClipboard = _ => throw new InvalidOperationException("busy clipboard") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.RunAsync());
        Assert.Equal(0, fixture.PasteCalls);
        Assert.Equal(fixture.Images, fixture.Staging.Drain(10));
    }

    [Fact]
    public async Task FocusExceptionRestoresUnpastedImages()
    {
        var fixture = new Fixture
        {
            Focus = call => call == 1 ? Task.FromResult(true) : throw new InvalidOperationException("browser closed")
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.RunAsync());
        Assert.Equal(0, fixture.PasteCalls);
        Assert.Equal(fixture.Images, fixture.Staging.Drain(10));
    }

    [Fact]
    public async Task FailedWaitAfterPasteDoesNotRepeatTheDispatchedImage()
    {
        var fixture = new Fixture
        {
            Delay = milliseconds => milliseconds == 710
                ? throw new InvalidOperationException("shutdown") : Task.CompletedTask
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.RunAsync());
        Assert.Equal(1, fixture.PasteCalls);
        Assert.Equal(new[] { fixture.Images[1] }, fixture.Staging.Drain(10));
    }

    [Fact]
    public async Task SuccessfulTransferFocusesBeforeEveryPasteAndEmptiesStaging()
    {
        var fixture = new Fixture();
        Assert.Equal(2, await fixture.RunAsync());
        Assert.Equal(3, fixture.FocusCalls);
        Assert.Equal(2, fixture.PasteCalls);
        Assert.Equal(fixture.Images, fixture.ClipboardImages);
        Assert.Equal(0, fixture.Staging.Count);
    }

    [Fact]
    public async Task RestoredImagesRemainAheadOfImagesOutsideTheRequestedBatch()
    {
        var fixture = new Fixture { Focus = call => Task.FromResult(call == 1) };
        Assert.Equal(0, await fixture.RunAsync(1));
        Assert.Equal(fixture.Images, fixture.Staging.Drain(10));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 10)]
    public async Task TransferSizeIsClampedToSupportedRange(int max, int expected)
    {
        var fixture = new Fixture(10);
        Assert.Equal(expected, await fixture.RunAsync(max));
        Assert.Equal(10 - expected, fixture.Staging.Count);
    }

    private sealed class Fixture
    {
        internal PortalStaging Staging { get; } = new();
        internal BitmapSource[] Images { get; }
        internal List<BitmapSource> ClipboardImages { get; } = new();
        internal bool Allowed { get; set; } = true;
        internal bool RestoreAllowed { get; set; } = true;
        internal int FocusCalls { get; private set; }
        internal int PasteCalls { get; private set; }
        internal Func<int, Task<bool>> Focus { get; set; } = _ => Task.FromResult(true);
        internal Func<int, Task> Delay { get; set; } = _ => Task.CompletedTask;
        internal Action<BitmapSource>? SetClipboard { get; set; }

        internal Fixture(int count = 2)
        {
            Images = Enumerable.Range(0, count).Select(index =>
            {
                var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null,
                    new byte[] { (byte)index, 0, 0, 255 }, 4);
                image.Freeze();
                Staging.TryAdd(image);
                return image;
            }).ToArray();
        }

        internal Task<int> RunAsync(int max = 10) => PortalImageTransfer.PasteAsync(
            Staging, max, () => Allowed, () => RestoreAllowed,
            () => Focus(++FocusCalls),
            image => { SetClipboard?.Invoke(image); ClipboardImages.Add(image); },
            () => PasteCalls++, Delay);
    }
}
