using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tropico.Analysis;
using Tropico.Data;
using Tropico.Desktop.Controls;
using Tropico.Desktop.Services;
using Tropico.Localization;

namespace Tropico.Desktop.ViewModels;

/// <summary>A figure at two moments. <see cref="IsGood"/> / <see cref="IsBad"/> tell whether the change is an improvement (both false when neutral).</summary>
public sealed record ChangeRow(string Name, string Before, string Now, string Change, bool IsGood, bool IsBad);

public sealed record SnapshotRow(string Save, string GameDate, string AnalysedOn);

public sealed class EvolutionViewModel
{
    // Whether a higher value is an improvement, per metric. Building counts are shown without colour.
    private static readonly Dictionary<string, bool> HigherIsBetter = new()
    {
        ["buildings"] = true, ["citizens"] = true, ["adults"] = true, ["children"] = true, ["unemployed"] = false,
        ["homelessFamilies"] = false, ["happiness"] = true, ["treasury"] = true, ["yearlyRevenue"] = true,
        ["yearlyExpenses"] = false, ["wages"] = false, ["upkeep"] = false,
    };

    public EvolutionViewModel(HistoryResult? history, ILocalizer? localizer = null)
    {
        var loc = localizer ?? Localizer.English;
        var culture = loc.Culture;
        history ??= HistoryResult.Of(HistoryStatus.Unavailable);

        HasComparison = history.Evolution is not null;
        Message = history.Status switch
        {
            HistoryStatus.Earliest => loc.Get("vm.evolution.earliest"),
            HistoryStatus.NoGameDate => loc.Get("vm.evolution.noGameDay"),
            HistoryStatus.Unavailable => loc.Get("vm.evolution.unavailable"),
            _ => "",
        };

        if (history.Evolution is { } evolution)
        {
            var previous = evolution.Previous;
            ComparedWith = loc.Format("vm.evolution.comparedWith", previous.SaveName, GameDate(previous.GameYear, previous.GameMonth, culture), evolution.MonthsBetween);
            Metrics = evolution.Metrics.Select(m => Metric(m, loc)).ToList();
            BuildingChanges = evolution.BuildingChanges
                .Select(c => new ChangeRow(DisplayNames.Building(c.Name), c.Before.ToString("N0", culture), c.After.ToString("N0", culture), Signed(c.Delta, "N0", culture), false, false))
                .ToList();
            FactionChanges = evolution.FactionChanges
                .Select(f => new ChangeRow(loc.Term(Term.Faction(f.Metric)), Number(f.Before, "0.0", culture), Number(f.After, "0.0", culture), Signed(f.Delta, "0.0", culture),
                    f.Delta > 0, f.Delta < 0))
                .ToList();
            NewFindings = evolution.NewFindings.Select(f => Finding(f, loc)).ToList();
            ResolvedFindings = evolution.ResolvedFindings.Select(f => Finding(f, loc)).ToList();
        }
        else
        {
            ComparedWith = "";
            Metrics = [];
            BuildingChanges = [];
            FactionChanges = [];
            NewFindings = [];
            ResolvedFindings = [];
        }

        Snapshots = history.Snapshots
            .Select(s => new SnapshotRow(s.SaveName, GameDate(s.GameYear, s.GameMonth, culture), s.AnalyzedAt.ToLocalTime().ToString("d", culture)))
            .ToList();
    }

    public bool HasComparison { get; }
    public string Message { get; }
    public string ComparedWith { get; }
    public IReadOnlyList<ChangeRow> Metrics { get; }
    public IReadOnlyList<ChangeRow> BuildingChanges { get; }
    public IReadOnlyList<ChangeRow> FactionChanges { get; }
    public IReadOnlyList<FindingViewModel> NewFindings { get; }
    public IReadOnlyList<FindingViewModel> ResolvedFindings { get; }
    public IReadOnlyList<SnapshotRow> Snapshots { get; }

    public bool HasBuildingChanges => BuildingChanges.Count > 0;
    public bool HasFactionChanges => FactionChanges.Count > 0;
    public bool HasNewFindings => NewFindings.Count > 0;
    public bool HasResolvedFindings => ResolvedFindings.Count > 0;
    public bool HasSnapshots => Snapshots.Count > 0;

    private static ChangeRow Metric(MetricChange change, ILocalizer loc)
    {
        var culture = loc.Culture;
        var format = change.Metric == "happiness" ? "0.0" : "N0";
        var better = HigherIsBetter.TryGetValue(change.Metric, out var higher) ? higher : (bool?)null;
        var improved = change.Delta is { } d and not 0 && better is { } b ? (d > 0) == b : (bool?)null;

        return new ChangeRow(
            loc.Term(new Term("metric", change.Metric)),
            Number(change.Before, format, culture), Number(change.After, format, culture), Signed(change.Delta, format, culture),
            improved == true, improved == false);
    }

    // Stored findings only keep their message: the evidence and the lead are not part of the comparison.
    private static FindingViewModel Finding(StoredFinding stored, ILocalizer loc) =>
        new(new Finding(stored.Code, stored.Severity, stored.Confidence, stored.Message) { Category = stored.Category }, loc);

    private static string Number(double? value, string format, CultureInfo culture) => value is { } v ? v.ToString(format, culture) : "?";

    // "+1,250" / "-3" / "0"; the sign makes an increase and a decrease obvious without colour.
    private static string Signed(double? value, string format, CultureInfo culture)
    {
        if (value is not { } v) return "?";
        var text = System.Math.Abs(v).ToString(format, culture);
        return v > 0 && text != "0" && text != "0.0" ? "+" + text : v < 0 && text != "0" && text != "0.0" ? "-" + text : "0";
    }

    private static string GameDate(int? year, int? month, CultureInfo culture) =>
        year is { } y && month is { } m and >= 1 and <= 12 ? ChartScale.FormatDate((y, m), culture) : "-";
}
