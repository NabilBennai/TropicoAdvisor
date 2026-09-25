using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tropico.Analysis;

namespace Tropico.Desktop.ViewModels;

public sealed class FindingViewModel(Finding finding)
{
    public string Message => finding.Message;
    public string Label => $"{finding.Severity} · {finding.Confidence}";
    public bool IsWarning => finding.Severity == Severity.Warning;
    public bool IsCritical => finding.Severity == Severity.Critical;

    /// <summary>Findings on unverified data are shown dimmed.</summary>
    public bool IsUncertain => finding.Confidence == Confidence.Uncertain;

    public string Category => finding.Category;
    public string? Suggestion => finding.Suggestion;
    public bool HasSuggestion => !string.IsNullOrEmpty(finding.Suggestion);
    public IReadOnlyList<string> Evidence => finding.Evidence;
    public bool HasEvidence => finding.Evidence.Count > 0;
}

public sealed class IslandReportViewModel
{
    private const int TopBuildingClasses = 8;

    public IslandReportViewModel(IslandReport report)
    {
        var snapshot = report.Snapshot;

        Title = snapshot.SaveName ?? "(unnamed save)";
        GameBuild = snapshot.GameBuild;
        Buildings = snapshot.Buildings.Total.ToString("N0", CultureInfo.CurrentCulture);
        Citizens = Format(snapshot.Population.Total);
        Treasury = Format(snapshot.Treasury);
        TopBuildings = snapshot.Buildings.ByClass
            .OrderByDescending(c => c.Value).ThenBy(c => c.Key)
            .Take(TopBuildingClasses)
            .Select(c => $"{c.Key}: {c.Value:N0}")
            .ToList();
        Findings = report.Findings.Select(f => new FindingViewModel(f)).ToList();
        Economy = new EconomyViewModel(snapshot, report.Findings);
        Trade = new TradeViewModel(snapshot, report.Findings);
        Population = new PopulationViewModel(snapshot, report.Findings);
    }

    public PopulationViewModel Population { get; }

    public EconomyViewModel Economy { get; }
    public TradeViewModel Trade { get; }

    public string Title { get; }
    public string GameBuild { get; }
    public string Buildings { get; }
    public string Citizens { get; }
    public string Treasury { get; }
    public IReadOnlyList<string> TopBuildings { get; }
    public IReadOnlyList<FindingViewModel> Findings { get; }
    public bool HasFindings => Findings.Count > 0;

    private static string Format(double? value) => value is { } v ? v.ToString("N0", CultureInfo.CurrentCulture) : "?";

    private static string Format(int? value) => value is { } v ? v.ToString("N0", CultureInfo.CurrentCulture) : "?";
}
