using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tropico.Desktop.Controls;

/// <summary>Pure chart maths (no UI types), so it can be unit tested.</summary>
public static class ChartScale
{
    private const double Padding = 0.08;

    /// <summary>Value range with 8 % padding; a constant series gets a range around its value so nothing divides by zero.</summary>
    public static (double Min, double Max) Range(IEnumerable<double> values)
    {
        var finite = values.Where(double.IsFinite).ToList();
        if (finite.Count == 0) return (0, 1);

        var (min, max) = (finite.Min(), finite.Max());
        if (min == max)
        {
            var margin = Math.Max(1, Math.Abs(min) * 0.05);
            return (min - margin, max + margin);
        }

        var pad = (max - min) * Padding;
        return (min - pad, max + pad);
    }

    /// <summary>X position of sample <paramref name="index"/> among <paramref name="count"/> samples spread over <paramref name="width"/>.</summary>
    public static double MapX(int index, int count, double width) => count <= 1 ? width / 2 : index * width / (count - 1);

    /// <summary>Y position (0 = top) of a value in [min, max] over <paramref name="height"/>.</summary>
    public static double MapY(double value, double min, double max, double height) =>
        max <= min ? height / 2 : height - (value - min) / (max - min) * height;

    /// <summary>Compact axis label: 1.45M, 12.3k, 1,234, 8.97.</summary>
    public static string FormatValue(double value)
    {
        var abs = Math.Abs(value);
        return abs switch
        {
            >= 1_000_000 => (value / 1_000_000).ToString("0.##", CultureInfo.InvariantCulture) + "M",
            >= 10_000 => (value / 1_000).ToString("0.#", CultureInfo.InvariantCulture) + "k",
            >= 1_000 => value.ToString("N0", CultureInfo.InvariantCulture),
            >= 100 => value.ToString("0", CultureInfo.InvariantCulture),
            _ => value.ToString("0.##", CultureInfo.InvariantCulture),
        };
    }

    private static readonly string[] MonthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public static string FormatDate((int Year, int Month) date) => $"{MonthNames[date.Month - 1]} {date.Year}";
}
