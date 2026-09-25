using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using Tropico.Analysis;
using Tropico.Desktop.Controls;

namespace Tropico.Desktop.ViewModels;

/// <summary>A labelled value with a bar; <see cref="Fraction"/> is in [0, 1].</summary>
public sealed record BarRow(string Label, string Value, double Fraction);

public sealed class PopulationViewModel
{
    private static readonly Color Blue = Color.Parse("#29C4C4");
    private static readonly Color Orange = Color.Parse("#F2B84B");
    private static readonly Color Red = Color.Parse("#E4572E");
    private static readonly Color Green = Color.Parse("#3DB37A");

    public PopulationViewModel(IslandSnapshot snapshot, IReadOnlyList<Finding> findings)
    {
        var population = snapshot.Population;
        var details = snapshot.PopulationInfo;

        Citizens = Format(population.Total);
        AgeSummary = $"{Format(population.Adults)} adults · {Format(population.Children)} children · {Format(population.Retired)} retired";
        Unemployed = details.Unemployed.Count == 0 ? "?" : FormatNumber(details.TotalUnemployed);
        HomelessFamilies = details.HomelessFamilies.Count == 0 ? "?" : FormatNumber(details.TotalHomelessFamilies);
        HomelessCitizens = details.CitizensLookingForHome is { } looking ? $"about {FormatNumber(looking)} citizens looking for a home" : "";
        Happiness = details.HappinessOverall is { } happiness ? happiness.ToString("0", CultureInfo.CurrentCulture) + " / 100" : "?";

        PopulationSeries = [new ChartSeries("Population", Values(details.PopulationHistory), Blue)];
        JobsHousingSeries =
        [
            new ChartSeries("Unemployed", Values(details.UnemployedHistory), Orange),
            new ChartSeries("Homeless families", Values(details.HomelessFamiliesHistory), Red),
            new ChartSeries("Vacant homes", Values(details.VacantHomesHistory), Green),
        ];
        HappinessSeries = [new ChartSeries("Overall happiness", Values(details.HappinessHistory), Blue)];
        (Left, Right) = Range(details, details.PopulationHistory);

        HappinessCategories = details.HappinessByCategory
            .OrderBy(c => c.Value).ThenBy(c => c.Key, System.StringComparer.Ordinal)
            .Select(c => new BarRow(c.Key, c.Value.ToString("0.0", CultureInfo.CurrentCulture), System.Math.Clamp(c.Value / 100, 0, 1)))
            .ToList();
        Education = Distribution(PopulationDetails.EducationLevels, details.Education);
        AgeGroups = Distribution(PopulationDetails.AgeGroupNames, details.AgeGroups);
        Wealth = Distribution(Enumerable.Range(1, details.Wealth.Count).Select(i => $"Class {i}").ToList(), details.Wealth);
        Homes = HousingRows(details);
        Findings = findings.Where(f => f.Category is "Population" or "Housing").Select(f => new FindingViewModel(f)).ToList();
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

    internal static (string? Left, string? Right) Range(PopulationDetails details, IReadOnlyList<TimePoint> history)
    {
        if (history.Count == 0) return (null, null);

        string Label(double x) => details.DateOf(x) is { } date ? ChartScale.FormatDate(date) : $"month {x:0}";
        return (Label(history[0].X), Label(history[^1].X));
    }

    // Bars relative to the group total.
    private static List<BarRow> Distribution(IReadOnlyList<string> labels, IReadOnlyList<double> values)
    {
        var total = values.Sum();
        return labels.Zip(values, (label, value) => new BarRow(
                label, $"{value.ToString("N0", CultureInfo.CurrentCulture)} ({(total > 0 ? value / total : 0).ToString("P0", CultureInfo.CurrentCulture)})",
                total > 0 ? value / total : 0))
            .ToList();
    }

    // Homeless families and vacant homes side by side for each housing tier, bars relative to the largest value.
    private static List<BarRow> HousingRows(PopulationDetails details)
    {
        var max = details.HomelessFamilies.Concat(details.VacantHomes).DefaultIfEmpty(0).Max();
        var rows = new List<BarRow>();
        for (var i = 0; i < PopulationDetails.HousingTiers.Length; i++)
        {
            var homeless = i < details.HomelessFamilies.Count ? details.HomelessFamilies[i] : 0;
            var vacant = i < details.VacantHomes.Count ? details.VacantHomes[i] : 0;
            if (homeless == 0 && vacant == 0) continue;

            rows.Add(new BarRow($"{PopulationDetails.HousingTiers[i]}: homeless families", homeless.ToString("N0", CultureInfo.CurrentCulture), max > 0 ? homeless / max : 0));
            rows.Add(new BarRow($"{PopulationDetails.HousingTiers[i]}: vacant homes", vacant.ToString("N0", CultureInfo.CurrentCulture), max > 0 ? vacant / max : 0));
        }

        return rows;
    }

    private static string Format(int? value) => value is { } v ? v.ToString("N0", CultureInfo.CurrentCulture) : "?";

    private static string FormatNumber(double value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
