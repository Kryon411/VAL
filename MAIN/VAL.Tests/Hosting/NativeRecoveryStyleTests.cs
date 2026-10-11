using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using VAL.App.UI;

using Xunit;

namespace VAL.Tests.Hosting;

public sealed class NativeRecoveryStyleTests
{
    private static readonly string[] StateLabels = ["Rest", "Hover", "Pressed", "Selected", "Disabled", "Keyboard focus"];
    [Theory]
    [InlineData(180, 32, 999, 4, 16, 11)]
    [InlineData(48, 48, 999, 4, 24, 19)]
    [InlineData(360, 180, 22, 3.2, 22, 17.8)]
    [InlineData(32, 28, 999, 2.6, 14, 10.4)]
    [InlineData(4, 4, 999, 4, 2, 0)]
    public void BezelGeometryKeepsFixedMetalBandAndClampedInnerRadius(
        double width, double height, double radius, double rim, double expectedOuter, double expectedInner)
    {
        var geometry = BezelChrome.GetGeometry(new Size(width, height), radius, rim);
        Assert.Equal(expectedOuter, geometry.OuterRadius, 6);
        Assert.Equal(expectedInner, geometry.InnerRadius, 6);
        Assert.Equal(rim + 1, geometry.Face.Left, 6);
        Assert.True(geometry.Face.Width >= 0 && geometry.Face.Height >= 0);
    }

    [Fact]
    public async Task ApprovedChromeRendersAllStatesAndWrapsLongLabelsWithoutChangingContent()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var canvas = new Canvas { Width = 460, Height = 420, Background = new SolidColorBrush(Color.FromRgb(24, 27, 31)) };
                var labels = StateLabels;
                for (var i = 0; i < labels.Length; i++)
                {
                    var chrome = new BezelChrome
                    {
                        Width = 400,
                        Height = 32,
                        Padding = new Thickness(14, 7, 14, 7),
                        IsHovered = i == 1,
                        IsPressed = i == 2,
                        IsSelected = i == 3,
                        IsEnabled = i != 4,
                        Opacity = i == 4 ? .44 : 1,
                        IsFocusVisible = i == 5,
                        Child = new TextBlock { Text = labels[i], FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(220, 228, 237)), HorizontalAlignment = HorizontalAlignment.Center },
                    };
                    Canvas.SetLeft(chrome, 20); Canvas.SetTop(chrome, 12 + i * 48);
                    canvas.Children.Add(chrome);
                }
                var label = new TextBlock
                {
                    Text = "Return to ChatGPT with an intentionally long label",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 16,
                    Foreground = Brushes.White,
                };
                var narrow = new BezelChrome { Width = 200, Height = 96, Padding = new Thickness(14, 7, 14, 7), Child = label };
                Canvas.SetLeft(narrow, 20); Canvas.SetTop(narrow, 310); canvas.Children.Add(narrow);
                canvas.Measure(new Size(460, 420)); canvas.Arrange(new Rect(0, 0, 460, 420)); canvas.UpdateLayout();
                Assert.True(label.ActualWidth <= 170);
                Assert.True(label.DesiredSize.Height > 20);
                var bitmap = new RenderTargetBitmap(460, 420, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(canvas);
                var pixels = new byte[460 * 420 * 4]; bitmap.CopyPixels(pixels, 460 * 4, 0);
                Assert.NotEqual(pixels[(28 * 460 + 60) * 4], pixels[(76 * 460 + 60) * 4]);
                var output = Environment.GetEnvironmentVariable("VAL_UI_SCREENSHOT_OUTPUT");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(output); encoder.Save(file);
                }
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RecoveryButtonUsesApprovedBezelWithExplicitTypographyAndKeyboardFocus()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var resources = (ResourceDictionary)Application.LoadComponent(
                    new Uri("/VAL.App;component/UI/VALWindowTheme.xaml", UriKind.Relative));
                var button = new Button
                {
                    Content = "Return to ChatGPT",
                    Style = (Style)resources["VAL.NativeRecoveryButton"],
                };
                button.ApplyTemplate();

                Assert.Equal(12, button.FontSize);
                Assert.Equal(FontWeights.Normal, button.FontWeight);
                Assert.Contains("Arial", button.FontFamily.Source);
                Assert.Equal(32, button.MinHeight);
                Assert.Equal(new Thickness(14, 7, 14, 7), button.Padding);
                Assert.IsType<LinearGradientBrush>(button.Background);
                var chrome = Assert.IsType<BezelChrome>(button.Template.FindName("RecoveryChrome", button));
                Assert.Equal(999, chrome.Radius);
                Assert.Equal(4, chrome.Rim);
                var focus = Assert.Single(button.Template.Triggers.OfType<Trigger>(),
                    trigger => trigger.Property == UIElement.IsKeyboardFocusedProperty);
                Assert.Contains(focus.Setters.OfType<Setter>(), setter =>
                    setter.TargetName == "RecoveryChrome" && setter.Property == BezelChrome.IsFocusVisibleProperty &&
                    Equals(setter.Value, true));
                button.IsEnabled = false;
                Assert.Equal(.44, button.Opacity);
                var toggle = new System.Windows.Controls.Primitives.ToggleButton
                {
                    Content = "On",
                    IsChecked = true,
                    Style = (Style)resources[typeof(System.Windows.Controls.Primitives.ToggleButton)],
                };
                toggle.ApplyTemplate();
                var toggleChrome = Assert.IsType<BezelChrome>(toggle.Template.FindName("ToggleChrome", toggle));
                Assert.True(toggleChrome.IsSelected);
                Assert.Equal(2.6, toggleChrome.Rim);
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
