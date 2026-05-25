using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
// WPF types only — disambiguate after UseWindowsForms pulled System.Drawing in.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace QuotaGlass.Widget.Controls;

/// <summary>
/// A simple percent-used ring. 0% = empty, 100% = full sweep. Track is drawn
/// behind the sweep so partial fills look like a "remaining quota" donut.
/// Color ramp: green &lt; 60% &lt; amber &lt; 85% &lt; red.
/// </summary>
public sealed class RadialRing : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent),
        typeof(double),
        typeof(RadialRing),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualPropertyChanged));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness),
        typeof(double),
        typeof(RadialRing),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualPropertyChanged));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Brush),
        typeof(RadialRing),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SafeBrushProperty = DependencyProperty.Register(
        nameof(SafeBrush),
        typeof(Brush),
        typeof(RadialRing),
        new FrameworkPropertyMetadata(Brushes.LightGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WarnBrushProperty = DependencyProperty.Register(
        nameof(WarnBrush),
        typeof(Brush),
        typeof(RadialRing),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DangerBrushProperty = DependencyProperty.Register(
        nameof(DangerBrush),
        typeof(Brush),
        typeof(RadialRing),
        new FrameworkPropertyMetadata(Brushes.Tomato, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Percent
    {
        get => (double)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush SafeBrush
    {
        get => (Brush)GetValue(SafeBrushProperty);
        set => SetValue(SafeBrushProperty, value);
    }

    public Brush WarnBrush
    {
        get => (Brush)GetValue(WarnBrushProperty);
        set => SetValue(WarnBrushProperty, value);
    }

    public Brush DangerBrush
    {
        get => (Brush)GetValue(DangerBrushProperty);
        set => SetValue(DangerBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = Math.Min(
            double.IsInfinity(availableSize.Width) ? 80 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 80 : availableSize.Height);
        return new Size(side, side);
    }

    /// <summary>
    /// NX-07: when true, suppress all ring animations. Bound to
    /// SystemParameters.ClientAreaAnimation / IsAnimationsEnabled in WPF.
    /// Currently the ring is static (no animations), so the toggle is a
    /// no-op today but reserved so v0.2+ ring transitions can honor it.
    /// </summary>
    public static readonly DependencyProperty ReducedMotionProperty = DependencyProperty.Register(
        nameof(ReducedMotion),
        typeof(bool),
        typeof(RadialRing),
        new FrameworkPropertyMetadata(false));

    public bool ReducedMotion
    {
        get => (bool)GetValue(ReducedMotionProperty);
        set => SetValue(ReducedMotionProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var side = Math.Min(ActualWidth, ActualHeight);
        if (side <= 0) return;

        var stroke = Math.Max(2, Thickness);
        var radius = (side / 2) - (stroke / 2);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);

        var trackPen = new Pen(TrackBrush, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        trackPen.Freeze();
        dc.DrawEllipse(null, trackPen, center, radius, radius);

        var clamped = Math.Clamp(Percent, 0.0, 100.0);
        if (clamped <= 0) return;

        var sweepDegrees = clamped / 100.0 * 360.0;
        var sweepBrush = clamped switch
        {
            >= 85 => DangerBrush,
            >= 60 => WarnBrush,
            _ => SafeBrush,
        };

        var sweepPen = new Pen(sweepBrush, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        sweepPen.Freeze();

        var startPoint = PointOnCircle(center, radius, -90);
        var endPoint = PointOnCircle(center, radius, -90 + sweepDegrees);
        var largeArc = sweepDegrees > 180;

        var figure = new PathFigure { StartPoint = startPoint, IsClosed = false };
        figure.Segments.Add(new ArcSegment(
            endPoint,
            new Size(radius, radius),
            rotationAngle: 0,
            isLargeArc: largeArc,
            sweepDirection: SweepDirection.Clockwise,
            isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();

        dc.DrawGeometry(null, sweepPen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        var rad = degrees * Math.PI / 180.0;
        return new Point(center.X + (radius * Math.Cos(rad)), center.Y + (radius * Math.Sin(rad)));
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((RadialRing)d).InvalidateVisual();
    }
}
