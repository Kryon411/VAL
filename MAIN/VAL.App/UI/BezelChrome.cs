using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VAL.App.UI;

/// <summary>Native counterpart of the approved VAL.Controls.css rim and glass face.</summary>
public sealed class BezelChrome : Decorator
{
    public static readonly DependencyProperty RimProperty = Register(nameof(Rim), 4d);
    public static readonly DependencyProperty RadiusProperty = Register(nameof(Radius), 999d);
    public static readonly DependencyProperty IsPanelProperty = Register(nameof(IsPanel), false);
    public static readonly DependencyProperty IsHoveredProperty = Register(nameof(IsHovered), false);
    public static readonly DependencyProperty IsPressedProperty = Register(nameof(IsPressed), false);
    public static readonly DependencyProperty IsSelectedProperty = Register(nameof(IsSelected), false);
    public static readonly DependencyProperty IsFocusVisibleProperty = Register(nameof(IsFocusVisible), false);
    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(BezelChrome),
        new FrameworkPropertyMetadata(default(Thickness), FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double Rim { get => (double)GetValue(RimProperty); set => SetValue(RimProperty, value); }
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public bool IsPanel { get => (bool)GetValue(IsPanelProperty); set => SetValue(IsPanelProperty, value); }
    public bool IsHovered { get => (bool)GetValue(IsHoveredProperty); set => SetValue(IsHoveredProperty, value); }
    public bool IsPressed { get => (bool)GetValue(IsPressedProperty); set => SetValue(IsPressedProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public bool IsFocusVisible { get => (bool)GetValue(IsFocusVisibleProperty); set => SetValue(IsFocusVisibleProperty, value); }
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    private static DependencyProperty Register<T>(string name, T value) => DependencyProperty.Register(
        name, typeof(T), typeof(BezelChrome), new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender));

    protected override Size MeasureOverride(Size constraint)
    {
        var p = Padding;
        Child?.Measure(new Size(Math.Max(0, constraint.Width - p.Left - p.Right - 2), Math.Max(0, constraint.Height - p.Top - p.Bottom - 2)));
        var desired = Child?.DesiredSize ?? default;
        return new Size(desired.Width + p.Left + p.Right + 2, desired.Height + p.Top + p.Bottom + 2);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var p = Padding;
        Child?.Arrange(new Rect(p.Left + 1, p.Top + 1,
            Math.Max(0, arrangeSize.Width - p.Left - p.Right - 2), Math.Max(0, arrangeSize.Height - p.Top - p.Bottom - 2)));
        return arrangeSize;
    }

    internal static (Rect Face, double OuterRadius, double InnerRadius) GetGeometry(Size size, double radius, double rim)
    {
        var outerRadius = Math.Max(0, Math.Min(radius, Math.Min(size.Width, size.Height) / 2));
        var inset = Math.Max(0, rim) + 1;
        var face = new Rect(inset, inset, Math.Max(0, size.Width - inset * 2), Math.Max(0, size.Height - inset * 2));
        return (face, outerRadius, Math.Max(0, Math.Min(Math.Max(2, outerRadius - inset), Math.Min(face.Width, face.Height) / 2)));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var dc = drawingContext;
        if (ActualWidth <= 2 || ActualHeight <= 2) return;
        var geometry = GetGeometry(RenderSize, Radius, Rim);
        var outer = new Rect(.5, .5, ActualWidth - 1, ActualHeight - 1);
        var r = geometry.OuterRadius;
        var face = geometry.Face;
        var ir = geometry.InnerRadius;
        var active = IsEnabled && IsPressed;
        var hovered = IsEnabled && IsHovered && !active;

        // Layered soft shadows approximate CSS blur without a shader/runtime dependency.
        for (var spread = 3; spread >= 1; spread--)
        {
            var shadow = outer;
            shadow.Inflate(spread / 2d, spread / 2d);
            shadow.Offset(0, active ? 1 : 2);
            dc.DrawRoundedRectangle(Solid(spread == 1 ? "#40000000" : "#12000000"), null, shadow, r + spread / 2d, r + spread / 2d);
        }

        dc.DrawRoundedRectangle(Linear(outer, 135,
            ("#66768b", 0), ("#3b4a5a", .13), ("#283747", .25), ("#080e16", .44),
            ("#0a111b", .62), ("#1a314a", .82), ("#0a111a", 1)), new Pen(Solid("#03070b"), 1), outer, r, r);
        dc.DrawRoundedRectangle(Linear(outer, 165,
            ("#0FE6F2FF", 0), ("#00E6F2FF", .31), ("#00000000", .76), ("#42000000", 1)), null, outer, r, r);
        DrawClippedEdge(dc, outer, r, new Rect(0, 0, ActualWidth, ActualHeight / 2), hovered ? "#58C0D5F0" : "#29A4B1C9", 1);
        DrawClippedEdge(dc, outer, r, new Rect(0, ActualHeight / 2, ActualWidth, ActualHeight / 2), "#BB000000", 1);
        if (face.Width <= 0 || face.Height <= 0) return;

        var fill = active ? Linear(face, 165, ("#070d16", 0), ("#111e2d", 1))
            : hovered ? Linear(face, 160, ("#192a3c", 0), ("#0d1826", 1))
            : IsSelected ? Linear(face, 160, ("#13243a", 0), ("#0d1826", 1))
            : IsPanel ? Linear(face, 145, ("#1b2531", 0), ("#111b27", 1))
            : Linear(face, 160, ("#152130", 0), ("#0a111b", 1));
        dc.DrawRoundedRectangle(fill, new Pen(Solid(active ? "#02050a" : "#030810"), 2), face, ir, ir);
        if (!active)
        {
            var sheen = new RadialGradientBrush
            {
                Center = new Point(IsSelected && !hovered ? .5 : IsPanel ? .15 : hovered ? .24 : .21, IsSelected && !hovered ? 1.1 : 0),
                GradientOrigin = new Point(IsSelected && !hovered ? .5 : IsPanel ? .15 : hovered ? .24 : .21, IsSelected && !hovered ? 1.1 : 0),
                RadiusX = .7,
                RadiusY = IsSelected && !hovered ? .76 : .62,
                GradientStops = new GradientStopCollection
                {
                    new(Color(IsSelected && !hovered ? "#7010477b" : hovered ? "#307CA9DC" : "#1B7594B6"), 0),
                    new(Colors.Transparent, 1),
                },
            };
            sheen.Freeze();
            dc.DrawRoundedRectangle(sheen, null, face, ir, ir);
        }
        dc.DrawRoundedRectangle(null, new Pen(Solid("#03070d"), 1), face, ir, ir);
        DrawClippedEdge(dc, face, ir, new Rect(0, 0, ActualWidth, ActualHeight / 2),
            active ? "#99000000" : hovered ? "#409EC7FB" : IsSelected ? "#2993BCED" : "#2591ADD0", active ? 3 : 1);
        DrawClippedEdge(dc, face, ir, new Rect(0, ActualHeight / 2, ActualWidth, ActualHeight / 2),
            active ? "#245e9b" : hovered || IsSelected ? "#347bcc" : "#193f6b", hovered || IsSelected ? 2 : 1);
        DrawClippedEdge(dc, face, ir, new Rect(ActualWidth / 2, 0, ActualWidth / 2, ActualHeight),
            hovered || IsSelected ? "#1b4c7d" : "#59174D8E", 1);
        if (IsFocusVisible) dc.DrawRoundedRectangle(null, new Pen(Solid("#79b8ff"), 2), outer, r, r);
    }

    private static void DrawClippedEdge(DrawingContext dc, Rect rect, double radius, Rect clip, string color, double thickness)
    {
        dc.PushClip(new RectangleGeometry(clip));
        dc.DrawRoundedRectangle(null, new Pen(Solid(color), thickness), rect, radius, radius);
        dc.Pop();
    }

    private static Color Color(string value) => (Color)ColorConverter.ConvertFromString(value);
    private static SolidColorBrush Solid(string value)
    {
        var brush = new SolidColorBrush(Color(value));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush Linear(Rect rect, double angle, params (string Color, double Offset)[] stops)
    {
        var radians = angle * Math.PI / 180;
        var direction = new Vector(Math.Sin(radians), -Math.Cos(radians));
        var length = Math.Abs(rect.Width * direction.X) + Math.Abs(rect.Height * direction.Y);
        var center = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = center - direction * (length / 2),
            EndPoint = center + direction * (length / 2),
        };
        foreach (var stop in stops) brush.GradientStops.Add(new GradientStop(Color(stop.Color), stop.Offset));
        brush.Freeze();
        return brush;
    }
}
