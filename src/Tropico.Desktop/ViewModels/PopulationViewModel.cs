using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using Tropico.Analysis;
using Tropico.Desktop.Controls;
using Tropico.Localization;

namespace Tropico.Desktop.ViewModels;

/// <summary>A labelled value with a bar; <see cref="Fraction"/> is in [0, 1].</summary>
public sealed record BarRow(string Label, string Value, double Fraction);

public sealed class PopulationViewModel
{
    private static readonly Color Turquoise = Color.Parse("#29C4C4");
    private static readonly Color Gold = Color.Parse("#F2B84B");
    private static readonly Color Coral = Color.Parse("#E4572E");
    private static readonly Color Green = Color.Parse("#3DB37A");

    public PopulationViewModel(IslandSnapshot snapshot, IReadOnlyList<Finding> findings, ILocalizer? localizer = null)
    {
        var loc = localizer ?? Localizer.English;
        var culture = loc.Culture;
        var population = snapshot.Population;
        var details = snapshot.PopulationInfo;

        Citizens = Format(population.Total, culture);
        AgeSummary = loc.Format("vm.ageSummary", Format(population.Adults, culture), Format(population.Children, culture), Format(population.Retired, culture));
        Unemployed = details.Unemployed.Count == 0 ? "?" : FormatNumber(details.TotalUnemployed, culture);
        HomelessFamilies = details.HomelessFamilies.Count == 0 ? "?" : FormatNumber(details.TotalHomelessFamilies, culture);
        HomelessCitizens = details.CitizensLookingForHome is { } looking ? loc.Format("vm.lookingForHome", FormatNumber(looking, culture)) : "";
        Happiness = details.HappinessOverall is { } happiness ? loc.Format("vm.outOf100", happiness.ToString("0", culture)) : "?";

        PopulationSeries = [new ChartSeries(loc.Get("ui.population"), Values(details.PopulationHistory), Turquoise)];
        JobsHousingSeries =
        [
            new ChartSeries(loc.Get("ui.unemployed"), Values(details.UnemployedHistory), Gold),
            new ChartSeries(loc.Get("ui.homelessFamilies"), Values(details.HomelessFamiliesHistory), Coral),
            new ChartSeries(loc.Get("evidence.vacantHomes"), Values(details.VacantHomesHistory), Green),
        ];
        HappinessSeries = [new ChartSeries(loc.Get("ui.overallHappiness"), Values(details.HappinessHistory), Gold)];
        (Left, Right) = Range(details, details.PopulationHistory, loc);

        HappinessCategories = details.HappinessByCategory
            .OrderBy(c => c.Value).ThenBy(c => c.Key, System.StringComparer.Ordinal)
            .Select(c => new BarRow(loc.Term(new Term("happiness", c.Key)), c.Value.ToString("0.0", culture), System.Math.Clamp(c.Value / 100, 0, 1)))
            .ToList();
        Education = Distribution(PopulationDetails.EducationLevels.Select(n => loc.Term(new Term("education", n))).ToList(), details.Education, culture);
        AgeGroups = Distribution(PopulationDetails.AgeGroupNames.Select(n => loc.Term(new Term("ageGroup", n))).ToList(), details.AgeGroups, culture);
        Wealth = Distribution(Enumerable.Range(1, details.Wealth.Count).Select(i => loc.Format("vm.wealthClass", i)).ToList(), details.Wealth, culture);
        Homes = HousingRows(details, loc);
        Findings = findings.Where(f => f.Category is "Population" or "Housing").Select(f => new FindingViewModel(f, loc)).ToList();
    }

    public string Citizens { get; }
    public string AgeSummary { get; }
    public string Unemployed { get; }
    public string HomelessFamilies { get; }
    public string HomelessCitizens { get; }
    public string Happiness { get; }

    public IReadOnlyList<ChartSeries> PopulationSeries { get; }
    public IReadOnlyList<ChartSeries> JobsHousingSeries { get; }
    public IReadOnlyList<ChartSeries> HappinessSeries { get; }
    public string? Left { get; }
    public string? Right { get; }

    public IReadOnlyList<BarRow> HappinessCategories { get; }
    public IReadOnlyList<BarRow> Education { get; }
    public IReadOnlyList<BarRow> AgeGroups { get; }
    public IReadOnlyList<BarRow> Wealth { get; }
    public IReadOnlyList<BarRow> Homes { get; }
    public IReadOnlyList<FindingViewModel> Findings { get; }
    public bool HasFindings => Findings.Count > 0;

    private static List<double> Values(IReadOnlyList<TimePoint> points) => points.Select(p => p.Value).ToList();

    internal static (string? Left, string? Right) Range(PopulationDetails details, IReadOnlyList<TimePoint> history, ILocalizer loc)
    {
        if (history.Count == 0) return (null, null);

        string Label(double x) => details.DateOf(x) is { } date ? ChartScale.FormatDate(date, loc.Culture) : loc.Format("vm.monthFallback", x);
        return (Label(history[0].X), Label(history[^1].X));
    }

    // Bars relative to the group total.
    private static List<BarRow> Distribution(IReadOnlyList<string> labels, IReadOnlyList<double> values, CultureInfo culture)
    {
        var total = values.Sum();
        return labels.Zip(values, (label, value) => new BarRow(
                label, $"{value.ToString("N0", culture)} ({(total > 0 ? value / total : 0).ToString("P0", culture)})",
                total > 0 ? value / total : 0))
            .ToList();
    }

    // Homeless families and vacant homes side by side for each housing tier, bars relative to the largest value.
    private static List<BarRow> HousingRows(PopulationDetails details, ILocalizer loc)
    {
        var culture = loc.Culture;
        var max = details.HomelessFamilies.Concat(details.VacantHomes).DefaultIfEmpty(0).Max();
        var rows = new List<BarRow>();
        for (var i = 0; i < PopulationDetails.HousingTiers.Length; i++)
        {
            var homeless = i < details.HomelessFamilies.Count ? details.HomelessFamilies[i] : 0;
            var vacant = i < details.VacantHomes.Count ? details.VacantHomes[i] : 0;
            if (homeless == 0 && vacant == 0) continue;

            var tier = loc.Term(new Term("tier", PopulationDetails.HousingTiers[i]));
            rows.Add(new BarRow(loc.Format("vm.homesHomeless", tier), homeless.ToString("N0", culture), max > 0 ? homeless / max : 0));
            rows.Add(new BarRow(loc.Format("vm.homesVacant", tier), vacant.ToString("N0", culture), max > 0 ? vacant / max : 0));
        }

        return rows;
    }

    private static string Format(int? value, CultureInfo culture) => value is { } v ? v.ToString("N0", culture) : "?";

    private static string FormatNumber(double value, CultureInfo culture) => value.ToString("N0", culture);
}
