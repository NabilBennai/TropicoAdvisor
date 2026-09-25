using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Tropico.Desktop.Controls;

public sealed record ChartSeries(string Name, IReadOnlyList<double> Values, Color Color);

/// <summary>
/// Minimal dependency-free line chart: one or several series sharing the X positions (one sample per month), a light grid with min/max
/// labels, a legend and the first/last date labels. Text and grid follow the theme foreground.
/// </summary>
public sealed class LineChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ChartSeries>?> SeriesProperty =
        AvaloniaProperty.Register<LineChart, IReadOnlyList<ChartSeries>?>(nameof(Series));

    public static readonly StyledProperty<string?> LeftLabelProperty = AvaloniaProperty.Register<LineChart, string?>(nameof(LeftLabel));

    public static readonly StyledProperty<string?> RightLabelProperty = AvaloniaProperty.Register<LineChart, string?>(nameof(RightLabel));

    private const double AxisWidth = 52;
    private const double TopMargin = 22;
    private const double BottomMargin = 20;
    private const double FontSize = 11;

    static LineChart()
    {
        AffectsRender<LineChart>(SeriesProperty, LeftLabelProperty, RightLabelProperty);
    }

    public IReadOnlyList<ChartSeries>? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    /// <summary>Label under the first sample (e.g. the oldest month).</summary>
    public string? LeftLabel
    {
        get => GetValue(LeftLabelProperty);
        set => SetValue(LeftLabelProperty, value);
    }

    /// <summary>Label under the last sample (e.g. the current month).</summary>
    public string? RightLabel
    {
        get => GetValue(RightLabelProperty);
        set => SetValue(RightLabelProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 180 : availableSize.Height);

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var series = Series?.Where(s => s.Values.Count > 0).ToList();
        var text = GetValue(TextElement.ForegroundProperty) ?? Brushes.Gray;
        var plot = new Rect(AxisWidth, TopMargin, Math.Max(0, Bounds.Width - AxisWidth - 8), Math.Max(0, Bounds.Height - TopMargin - BottomMargin));

        if (series is null or { Count: 0 } || plot.Width < 20 || plot.Height < 20)
        {
            DrawText(context, "No data", text, new Point(AxisWidth, TopMargin));
            return;
        }

        var (min, max) = ChartScale.Range(series.SelectMany(s => s.Values));
        var gridPen = new Pen(text, 1) { Brush = new SolidColorBrush(Colors.Gray, 0.25) };

        // horizontal grid with value labels (max, middle, min)
        foreach (var fraction in new[] { 0.0, 0.5, 1.0 })
        {
            var y = plot.Top + fraction * plot.Height;
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(context, ChartScale.FormatValue(max - fraction * (max - min)), text, new Point(2, y - FontSize / 2 - 2));
        }

        foreach (var s in series)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                for (var i = 0; i < s.Values.Count; i++)
                {
                    var point = new Point(
                        plot.Left + ChartScale.MapX(i, s.Values.Count, plot.Width),
                        plot.Top + ChartScale.MapY(s.Values[i], min, max, plot.Height));
                    if (i == 0) g.BeginFigure(point, false);
                    else g.LineTo(point);
                }

                g.EndFigure(false);
            }

            context.DrawGeometry(null, new Pen(new SolidColorBrush(s.Color), 2), geometry);
        }

        // legend
        var x = plot.Left;
        foreach (var s in series)
        {
            context.DrawRectangle(new SolidColorBrush(s.Color), null, new Rect(x, 6, 10, 10));
            var label = DrawText(context, s.Name, text, new Point(x + 14, 3));
            x += 14 + label + 14;
        }

        if (LeftLabel is not null) DrawText(context, LeftLabel, text, new Point(plot.Left, plot.Bottom + 3));
        if (RightLabel is not null)
        {
            var width = Measure(RightLabel, text);
            DrawText(context, RightLabel, text, new Point(plot.Right - width, plot.Bottom + 3));
        }
    }

    private static FormattedText Format(string value, IBrush brush) =>
        new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, FontSize, brush);

    private static double Measure(string value, IBrush brush) => Format(value, brush).Width;

    private static double DrawText(DrawingContext context, string value, IBrush brush, Point origin)
    {
        var formatted = Format(value, brush);
        context.DrawText(formatted, origin);
        return formatted.Width;
    }
}
