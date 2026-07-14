using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace VAL.App.Host.Services
{
    internal static class DesktopToastVisualFactory
    {
        private static readonly CornerRadius SoftGlassCorner = new(18);
        private static readonly Thickness SoftGlassPadding = new(16, 12, 16, 14);
        private static readonly Thickness SoftGlassMargin = new(0, 0, 0, 14);
        private static readonly ControlTemplate SoftGlassButtonTemplate = BuildSoftGlassButtonTemplate();

        public static Border CreateMessageToast(string title, string? subtitle)
        {
            return WrapInSoftGlassChrome(CreateTextContent(title, subtitle, alwaysShowTitle: false));
        }

        public static Border CreateActionToast(
            string title,
            string? subtitle,
            (string Label, Action OnClick)[] actions)
        {
            var content = CreateTextContent(title, subtitle, alwaysShowTitle: true);

            if (actions.Length > 0)
            {
                var buttonRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 12, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                foreach (var action in actions)
                {
                    buttonRow.Children.Add(CreateActionButton(action.Label, action.OnClick));
                }

                content.Children.Add(buttonRow);
            }

            return WrapInSoftGlassChrome(content);
        }

        private static StackPanel CreateTextContent(string title, string? subtitle, bool alwaysShowTitle)
        {
            var content = new StackPanel();

            if (alwaysShowTitle || !string.IsNullOrWhiteSpace(title))
            {
                content.Children.Add(new TextBlock
                {
                    Text = title ?? string.Empty,
                    Foreground = new SolidColorBrush(Color.FromArgb(176, 245, 251, 255)),
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                content.Children.Add(new TextBlock
                {
                    Text = subtitle,
                    Foreground = new SolidColorBrush(Color.FromArgb(143, 170, 190, 210)),
                    FontSize = 11,
                    Margin = new Thickness(0, 6, 0, 0),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            return content;
        }

        private static Button CreateActionButton(string label, Action onClick)
        {
            var button = new Button
            {
                Content = label ?? string.Empty,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(14, 6, 14, 6),
                MinWidth = 88,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };

            ApplySoftGlassButtonStyle(button);
            button.Click += (_, __) => onClick();
            return button;
        }

        private static LinearGradientBrush CreateSoftGlassCardBackground()
        {
            return new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(245, 17, 25, 34), 0.0),
                    new GradientStop(Color.FromArgb(245, 7, 15, 25), 1.0)
                }
            };
        }

        private static SolidColorBrush CreateSoftGlassBorderBrush()
        {
            return new SolidColorBrush(Color.FromArgb(41, 120, 180, 240));
        }

        private static Border WrapInSoftGlassChrome(UIElement content)
        {
            var border = new Border
            {
                MaxWidth = 420,
                MinWidth = 260,
                Background = CreateSoftGlassCardBackground(),
                BorderBrush = CreateSoftGlassBorderBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = SoftGlassCorner,
                Padding = SoftGlassPadding,
                Margin = SoftGlassMargin,
                Opacity = 0,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true,
                Child = content
            };

            border.Loaded += (_, __) =>
            {
                var dpi = VisualTreeHelper.GetDpi(border);
                var onePx = 1.0 / dpi.DpiScaleX;
                border.BorderThickness = new Thickness(onePx);
            };

            return border;
        }

        private static ControlTemplate BuildSoftGlassButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, SoftGlassCorner);
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            border.SetBinding(
                Border.BackgroundProperty,
                new Binding("Background")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });
            border.SetBinding(
                Border.BorderBrushProperty,
                new Binding("BorderBrush")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });
            border.SetBinding(
                Border.BorderThicknessProperty,
                new Binding("BorderThickness")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetBinding(
                ContentPresenter.ContentProperty,
                new Binding("Content")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });
            presenter.SetBinding(
                ContentPresenter.MarginProperty,
                new Binding("Padding")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });
            presenter.SetBinding(
                ContentPresenter.ContentTemplateProperty,
                new Binding("ContentTemplate")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });

            border.AppendChild(presenter);
            template.VisualTree = border;
            return template;
        }

        private static void ApplySoftGlassButtonStyle(Button button)
        {
            button.Template = SoftGlassButtonTemplate;
            button.Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(18, 255, 255, 255), 0.0),
                    new GradientStop(Color.FromArgb(245, 4, 18, 30), 1.0),
                }
            };
            button.Foreground = new SolidColorBrush(Color.FromArgb(176, 245, 251, 255));
            button.BorderBrush = new SolidColorBrush(Color.FromArgb(56, 120, 180, 240));
            button.BorderThickness = new Thickness(1);
        }
    }
}
