using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
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
        Severity = finding.Severity;
        Confidence = finding.Confidence;
        Category = finding.Category;
        Suggestion = finding.SuggestionIn(loc);
        Evidence = finding.EvidenceIn(loc);
    }

    public string Message { get; }
    public Severity Severity { get; }
    public Confidence Confidence { get; }
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

public sealed partial class IslandReportViewModel : ViewModelBase
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
        _allFindings = report.Findings.Select(f => new FindingViewModel(f, loc)).ToList();
        _shownOfTemplate = loc.Get("ui.shownOf");
        _culture = loc.Culture;
        FilterOptions = [loc.Get("ui.filterAll"), loc.Get("ui.filterWarnings"), loc.Get("ui.filterCritical")];
        ApplyFilter();
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
    private readonly IReadOnlyList<FindingViewModel> _allFindings;
    private readonly string _shownOfTemplate;
    private readonly CultureInfo _culture;

    /// <summary>Filter choices for the overview list: everything, warnings and critical, critical only.</summary>
    public IReadOnlyList<string> FilterOptions { get; }

    [ObservableProperty]
    public partial int SelectedFilter { get; set; }

    [ObservableProperty]
    public partial bool HideUncertain { get; set; }

    /// <summary>The overview suggestions after the filters.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<FindingViewModel> Findings { get; private set; } = [];

    [ObservableProperty]
    public partial string FilterSummary { get; private set; } = "";

    public bool HasFindings => Findings.Count > 0;

    /// <summary>The filter row is only useful when there is something to filter.</summary>
    public bool HasAnyFindings => _allFindings.Count > 0;

    partial void OnSelectedFilterChanged(int value) => ApplyFilter();

    partial void OnHideUncertainChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        var minimum = SelectedFilter switch { 1 => Severity.Warning, 2 => Severity.Critical, _ => Severity.Info };
        Findings = _allFindings.Where(f => f.Severity >= minimum && !(HideUncertain && f.IsUncertain)).ToList();
        FilterSummary = string.Format(_culture, _shownOfTemplate, Findings.Count, _allFindings.Count);
        OnPropertyChanged(nameof(HasFindings));
    }

    private static string Format(int? value, CultureInfo culture) => value is { } v ? v.ToString("N0", culture) : "?";
}
