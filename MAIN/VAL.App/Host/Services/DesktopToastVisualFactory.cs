using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using VAL.App.UI;

namespace VAL.App.Host.Services;

internal static class DesktopToastVisualFactory
{
    public static Border CreateMessageToast(string title, string? subtitle) =>
        WrapInChrome(CreateTextContent(title, subtitle, alwaysShowTitle: false));

    public static Border CreateActionToast(string title, string? subtitle, (string Label, Action OnClick)[] actions)
    {
        var content = CreateTextContent(title, subtitle, alwaysShowTitle: true);
        if (actions.Length > 0)
        {
            var buttonRow = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            foreach (var action in actions)
            {
                var button = new Button
                {
                    Content = action.Label ?? string.Empty,
                    Margin = new Thickness(8, 0, 0, 0),
                    Padding = new Thickness(14, 6, 14, 6),
                    MinWidth = 88,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                };
                button.SetResourceReference(FrameworkElement.StyleProperty, "VAL.ToolWindowSecondaryButton");
                button.Click += (_, __) => action.OnClick();
                buttonRow.Children.Add(button);
            }
            content.Children.Add(buttonRow);
        }
        return WrapInChrome(content);
    }

    private static StackPanel CreateTextContent(string title, string? subtitle, bool alwaysShowTitle)
    {
        var content = new StackPanel();
        if (alwaysShowTitle || !string.IsNullOrWhiteSpace(title))
        {
            content.Children.Add(new TextBlock
            {
                Text = title ?? string.Empty,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 228, 237)),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
        }
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            content.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = new SolidColorBrush(Color.FromRgb(168, 180, 194)),
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
        }
        return content;
    }

    private static Border WrapInChrome(UIElement content) => new()
    {
        MaxWidth = 420,
        MinWidth = 260,
        Margin = new Thickness(0, 0, 0, 14),
        Opacity = 0,
        SnapsToDevicePixels = true,
        UseLayoutRounding = true,
        Child = new BezelChrome
        {
            Radius = 22,
            Rim = 3.2,
            IsPanel = true,
            Padding = new Thickness(16, 12, 16, 14),
            Child = content,
        },
    };
}
