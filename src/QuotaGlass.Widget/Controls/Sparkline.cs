using System.Collections;
using System.Windows;
using System.Windows.Media;
using QuotaGlass.Widget.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace QuotaGlass.Widget.Controls;

/// <summary>
/// NX-08 — micro line-chart of recent <see cref="HistorySample"/> values.
/// No data label, no axes. Y range is normalized 0..100 so the sparkline
/// shape is comparable across buckets. Designed to fit under a bucket card
/// at ~120 × 24 px.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples),
        typeof(IEnumerable),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnRedraw));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.LightSteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineThicknessProperty = DependencyProperty.Register(
        nameof(LineThickness),
        typeof(double),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? Samples
    {
        get => (IEnumerable?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public double LineThickness
    {
        get => (double)GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Samples is null || ActualWidth <= 0 || ActualHeight <= 0) return;

        var values = ExtractPercents(Samples);
        if (values.Count < 2) return;

        var pen = new Pen(LineBrush, LineThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();

        var w = ActualWidth - 2;
        var h = ActualHeight - 2;

        var figure = new PathFigure { IsClosed = false };
        for (var i = 0; i < values.Count; i++)
        {
            var x = 1 + (i / (double)(values.Count - 1)) * w;
            var clamped = Math.Clamp(values[i], 0, 100);
            // 100% sits at the top of the sparkline (greater = closer to ceiling).
            var y = 1 + (1 - clamped / 100.0) * h;
            var p = new Point(x, y);
            if (i == 0) figure.StartPoint = p;
            else figure.Segments.Add(new LineSegment(p, isStroked: true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();

        dc.DrawGeometry(null, pen, geometry);
    }

    private static List<double> ExtractPercents(IEnumerable raw)
    {
        var result = new List<double>();
        foreach (var item in raw)
        {
            if (item is HistorySample sample)
            {
                result.Add(sample.PercentUsed);
            }
        }
        return result;
    }

    private static void OnRedraw(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((Sparkline)d).InvalidateVisual();
}
