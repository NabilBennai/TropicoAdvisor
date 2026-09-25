using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tropico.Analysis;
using Tropico.Desktop.Services;
using Tropico.Localization;

namespace Tropico.Desktop.ViewModels;

public sealed class FindingViewModel
{
    public FindingViewModel(Finding finding, ILocalizer? localizer = null)
    {
        var loc = localizer ?? Localizer.English;

        Message = finding.MessageIn(loc);
        Label = loc.Format("vm.findingLabel", new Term("severity", finding.Severity.ToString()), new Term("confidence", finding.Confidence.ToString()));
        IsWarning = finding.Severity == Severity.Warning;
        IsCritical = finding.Severity == Severity.Critical;
        IsUncertain = finding.Confidence == Confidence.Uncertain;
        Category = finding.Category;
        Suggestion = finding.SuggestionIn(loc);
        Evidence = finding.EvidenceIn(loc);
    }

    public string Message { get; }
    public string Label { get; }
    public bool IsWarning { get; }
    public bool IsCritical { get; }

    /// <summary>Findings on unverified data are shown dimmed.</summary>
    public bool IsUncertain { get; }

    public string Category { get; }
    public string? Suggestion { get; }
    public bool HasSuggestion => !string.IsNullOrEmpty(Suggestion);
    public IReadOnlyList<string> Evidence { get; }
    public bool HasEvidence => Evidence.Count > 0;
}

public sealed class IslandReportViewModel
{
    private const int TopBuildingClasses = 8;

    public IslandReportViewModel(IslandReport report, ILocalizer? localizer = null, HistoryResult? history = null)
    {
        var loc = localizer ?? Localizer.English;
        var snapshot = report.Snapshot;

        Title = snapshot.SaveName ?? loc.Get("vm.unnamedSave");
        GameBuild = snapshot.GameBuild;
        Buildings = snapshot.Buildings.Total.ToString("N0", loc.Culture);
        Citizens = Format(snapshot.Population.Total, loc.Culture);
        Treasury = snapshot.Treasury is { } treasury ? treasury.ToString("N0", loc.Culture) : "?";
        TopBuildings = snapshot.Buildings.ByClass
            .OrderByDescending(c => c.Value).ThenBy(c => c.Key)
            .Take(TopBuildingClasses)
            .Select(c => loc.Format("vm.classCount", c.Key, c.Value))
            .ToList();
        Findings = report.Findings.Select(f => new FindingViewModel(f, loc)).ToList();
        Economy = new EconomyViewModel(snapshot, report.Findings, loc);
        Trade = new TradeViewModel(snapshot, report.Findings, loc);
        Population = new PopulationViewModel(snapshot, report.Findings, loc);
        BuildingsTab = new BuildingsViewModel(snapshot, report.Findings, loc);
        PoliticsTab = new PoliticsViewModel(snapshot, report.Findings, loc);
        EvolutionTab = new EvolutionViewModel(history, loc);
    }

    public EvolutionViewModel EvolutionTab { get; }

    public PoliticsViewModel PoliticsTab { get; }
    public BuildingsViewModel BuildingsTab { get; }
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

    private static string Format(int? value, CultureInfo culture) => value is { } v ? v.ToString("N0", culture) : "?";
}
