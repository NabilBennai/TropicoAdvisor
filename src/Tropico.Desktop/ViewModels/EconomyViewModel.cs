using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using Tropico.Analysis;
using Tropico.Desktop.Controls;

namespace Tropico.Desktop.ViewModels;

public sealed record BreakdownRow(string Name, string Amount, string Share);

public sealed record CostCenterRow(string Name, string Buildings, string Wages, string Upkeep, string Income, string PerBuilding);

public sealed class EconomyViewModel
{
    private const int MaxCostRows = 12;

    private static readonly Color Blue = Color.Parse("#F2B84B");
    private static readonly Color Green = Color.Parse("#3DB37A");
    private static readonly Color Red = Color.Parse("#E4572E");

    public EconomyViewModel(IslandSnapshot snapshot, IReadOnlyList<Finding> findings)
    {
        var economy = snapshot.Economy;

        TreasurySeries = [new ChartSeries("Treasury", snapshot.TreasuryHistory.Select(p => p.Value).ToList(), Blue)];
        FlowSeries =
        [
            new ChartSeries("Revenue", economy.RevenueHistory.Select(p => p.Value).ToList(), Green),
            new ChartSeries("Expenses", economy.ExpenseHistory.Select(p => p.Value).ToList(), Red),
        ];
        (TreasuryLeft, TreasuryRight) = Range(economy, snapshot.TreasuryHistory);
        (FlowLeft, FlowRight) = Range(economy, economy.RevenueHistory);

        YearlyRevenue = Format(economy.YearlyRevenue);
        YearlyExpenses = Format(economy.YearlyExpenses);
        Balance = Format(economy.YearlyRevenue - economy.YearlyExpenses);
        Runway = snapshot.Treasury is { } treasury && economy.YearlyExpenses > 0
            ? $"{treasury / (economy.YearlyExpenses / 12.0):N0} months of spending"
            : "?";

        RevenueBreakdown = Breakdown(economy.RevenueByCategory);
        ExpenseBreakdown = Breakdown(economy.ExpensesByCategory);
        CostCenters = economy.ClassCosts
            .OrderByDescending(c => c.Cost).ThenBy(c => c.ClassName, System.StringComparer.Ordinal)
            .Where(c => c.Cost > 0 || c.FeesAndRents > 0)
            .Take(MaxCostRows)
            .Select(c => new CostCenterRow(
                DisplayNames.Building(c.ClassName),
                c.Instances.ToString("N0", CultureInfo.CurrentCulture),
                Format(c.Wages), Format(c.Upkeep), Format(c.FeesAndRents),
                c.Instances > 0 ? Format((double)c.Cost / c.Instances) : "-"))
            .ToList();
        Findings = findings.Where(f => f.Category == "Economy").Select(f => new FindingViewModel(f)).ToList();
    }

    public IReadOnlyList<ChartSeries> TreasurySeries { get; }
    public IReadOnlyList<ChartSeries> FlowSeries { get; }
    public string? TreasuryLeft { get; }
    public string? TreasuryRight { get; }
    public string? FlowLeft { get; }
    public string? FlowRight { get; }

    public string YearlyRevenue { get; }
    public string YearlyExpenses { get; }
    public string Balance { get; }
    public string Runway { get; }

    public IReadOnlyList<BreakdownRow> RevenueBreakdown { get; }
    public IReadOnlyList<BreakdownRow> ExpenseBreakdown { get; }
    public IReadOnlyList<CostCenterRow> CostCenters { get; }
    public IReadOnlyList<FindingViewModel> Findings { get; }
    public bool HasFindings => Findings.Count > 0;

    // First and last date of a history, as "Sep 1929" style labels, or month indices when the calendar is unknown.
    internal static (string? Left, string? Right) Range(EconomySnapshot economy, IReadOnlyList<TimePoint> history)
    {
        if (history.Count == 0) return (null, null);

        string Label(double x) => economy.DateOf(x) is { } date ? ChartScale.FormatDate(date) : $"month {x:0}";
        return (Label(history[0].X), Label(history[^1].X));
    }

    private static List<BreakdownRow> Breakdown(IReadOnlyDictionary<string, long> byCategory)
    {
        var total = byCategory.Values.Where(v => v > 0).Sum();
        return byCategory.Where(c => c.Value > 0)
            .OrderByDescending(c => c.Value).ThenBy(c => c.Key, System.StringComparer.Ordinal)
            .Select(c => new BreakdownRow(DisplayNames.Category(c.Key), Format(c.Value), ((double)c.Value / total).ToString("P0", CultureInfo.CurrentCulture)))
            .ToList();
    }

    private static string Format(double value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
