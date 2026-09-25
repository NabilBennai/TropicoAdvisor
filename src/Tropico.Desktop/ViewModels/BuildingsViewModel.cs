using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tropico.Analysis;

namespace Tropico.Desktop.ViewModels;

public sealed record BuildingClassRow(string Name, string Count, string Condition, string Workmode, string Budget, string Outputs, string Fill);

public sealed record ProductionRow(string Resource, string Producers, string Output, string Capacity, string Fill, string Full, string IslandStock, string Exported);

public sealed record DepositRow(string Resource, string Deposits, string InUse, string Producers, string Amount);

public sealed class BuildingsViewModel
{
    public BuildingsViewModel(IslandSnapshot snapshot, IReadOnlyList<Finding> findings)
    {
        var production = snapshot.Production;

        Total = snapshot.Buildings.Total.ToString("N0", CultureInfo.CurrentCulture);
        Producers = production.Producers.ToString("N0", CultureInfo.CurrentCulture);
        FullOutputs = production.FullOutputs.Count.ToString("N0", CultureInfo.CurrentCulture);
        NotBuilt = snapshot.Buildings.ByState
            .Where(s => s.Key is not ("Built" or IslandSnapshotBuilder.UnknownState)).Sum(s => s.Value).ToString("N0", CultureInfo.CurrentCulture);

        Classes = production.Classes.Select(c => new BuildingClassRow(
            DisplayNames.Building(c.ClassName),
            c.Count.ToString("N0", CultureInfo.CurrentCulture),
            c.States.Count == 0 ? "all built" : string.Join(", ", c.States.OrderBy(s => s.Key, System.StringComparer.Ordinal).Select(s => $"{s.Value} {s.Key}")),
            Dominant(c.Workmodes, DisplayNames.Workmode),
            Dominant(c.BudgetLevels, level => level),
            c.Outputs.Count == 0 ? "-" : string.Join(", ", c.Outputs.Select(DisplayNames.Category)),
            c.AverageOutputFill is { } fill ? fill.ToString("P0", CultureInfo.CurrentCulture) : "-")).ToList();

        Resources = production.Resources.Select(r => new ProductionRow(
            DisplayNames.Category(r.Resource),
            r.Producers.ToString("N0", CultureInfo.CurrentCulture),
            r.OutputStock.ToString("N0", CultureInfo.CurrentCulture),
            r.OutputCapacity.ToString("N0", CultureInfo.CurrentCulture),
            r.Fill.ToString("P0", CultureInfo.CurrentCulture),
            r.FullProducers > 0 ? r.FullProducers.ToString("N0", CultureInfo.CurrentCulture) : "-",
            r.IslandStock.ToString("N0", CultureInfo.CurrentCulture),
            r.ExportedLast12Months.ToString("N0", CultureInfo.CurrentCulture))).ToList();

        Deposits = production.Deposits.Select(d => new DepositRow(
            DisplayNames.Category(d.Resource),
            d.Deposits.ToString("N0", CultureInfo.CurrentCulture),
            d.Producers > 0 ? d.Tapped.ToString("N0", CultureInfo.CurrentCulture) : "0",
            d.Producers.ToString("N0", CultureInfo.CurrentCulture),
            d.AmountEach is { } amount ? amount.ToString("N0", CultureInfo.CurrentCulture) : "-")).ToList();

        Findings = findings.Where(f => f.Category is "Buildings" or "Production").Select(f => new FindingViewModel(f)).ToList();
    }

    public string Total { get; }
    public string Producers { get; }
    public string FullOutputs { get; }
    public string NotBuilt { get; }

    public IReadOnlyList<BuildingClassRow> Classes { get; }
    public IReadOnlyList<ProductionRow> Resources { get; }
    public IReadOnlyList<DepositRow> Deposits { get; }
    public IReadOnlyList<FindingViewModel> Findings { get; }
    public bool HasFindings => Findings.Count > 0;

    // Most common value, with the number of other values when the class is mixed ("Profit Protocol (+1 other)").
    public static string Dominant(IReadOnlyDictionary<string, int> counts, System.Func<string, string> format)
    {
        if (counts.Count == 0) return "-";

        var top = counts.OrderByDescending(c => c.Value).ThenBy(c => c.Key, System.StringComparer.Ordinal).First();
        var others = counts.Count - 1;
        return others == 0 ? format(top.Key) : $"{format(top.Key)} (+{others} other)";
    }
}
