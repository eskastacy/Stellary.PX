using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace Stellary.PX.Controls;

public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(ObservableCollection<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    public static readonly DependencyProperty SecondaryProperty = DependencyProperty.Register(
        nameof(Secondary), typeof(ObservableCollection<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WindowSizeProperty = DependencyProperty.Register(
        nameof(WindowSize), typeof(int), typeof(Sparkline),
        new FrameworkPropertyMetadata(48, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty Stroke2Property = DependencyProperty.Register(
        nameof(Stroke2), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public ObservableCollection<double>? Values
    {
        get => (ObservableCollection<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public ObservableCollection<double>? Secondary
    {
        get => (ObservableCollection<double>?)GetValue(SecondaryProperty);
        set => SetValue(SecondaryProperty, value);
    }

    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int WindowSize { get => (int)GetValue(WindowSizeProperty); set => SetValue(WindowSizeProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush Stroke2 { get => (Brush)GetValue(Stroke2Property); set => SetValue(Stroke2Property, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (Sparkline)d;
        if (e.OldValue is INotifyCollectionChanged old) old.CollectionChanged -= control.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged now) now.CollectionChanged += control.OnCollectionChanged;
        control.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 2 || h <= 2) return;

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), 1);
        for (var i = 1; i < 4; i++)
        {
            var y = h * i / 4.0;
            dc.DrawLine(gridPen, new Point(0, y), new Point(w, y));
        }

        DrawSeries(dc, Secondary, Stroke2, null, w, h, dashed: true);
        DrawSeries(dc, Values, Stroke, Fill, w, h, dashed: false);
    }

    private void DrawSeries(DrawingContext dc, ObservableCollection<double>? series, Brush stroke, Brush? fill, double w, double h, bool dashed)
    {
        if (series is null || series.Count < 2) return;
        var count = series.Count;
        var max = Maximum <= 0 ? 100 : Maximum;
        var window = Math.Max(2, WindowSize);

        // Until the window fills, stretch the available samples across the plot so a
        // freshly launched app reads as a live trace instead of a dot at the edge.
        var span = Math.Min(window, count);
        var step = w / (span - 1);

        // Newest sample sits on the right edge; the series grows leftwards as it fills.
        double X(int i) => w - (count - 1 - i) * step;

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(new Point(X(0), Y(series[0])), false, false);
            for (var i = 1; i < count; i++)
                ctx.LineTo(new Point(X(i), Y(series[i])), true, false);
        }
        line.Freeze();
        var pen = new Pen(stroke, dashed ? 1.1 : 1.35) { LineJoin = PenLineJoin.Round };
        if (dashed) pen.DashStyle = new DashStyle(new[] { 2.2, 2.6 }, 0);
        pen.Freeze();
        dc.DrawGeometry(null, pen, line);

        if (fill is null) return;
        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(X(0), h), true, true);
            ctx.LineTo(new Point(X(0), Y(series[0])), true, false);
            for (var i = 1; i < count; i++)
                ctx.LineTo(new Point(X(i), Y(series[i])), true, false);
            ctx.LineTo(new Point(X(count - 1), h), true, false);
        }
        area.Freeze();
        dc.DrawGeometry(fill, null, area);

        double Y(double value) => h - Math.Clamp(value, 0, max) / max * (h - 2) - 1;
    }
}
