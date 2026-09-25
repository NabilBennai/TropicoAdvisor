using Tropico.Analysis;
using Tropico.Desktop.ViewModels;

namespace Tropico.Desktop.Tests;

public class PopulationViewModelTests
{
    private static IslandSnapshot Snapshot() => new(
        "test", "t6", new BuildingSummary(10, new Dictionary<string, int>(), new Dictionary<string, int>()),
        new PopulationSummary(1300, 270, 950, 1, 0), 1_000, 0, [], [], [], [])
    {
        PopulationData = new PopulationDetails(
            [850, 450, 0], [270, 950, 1], [80, 220, 980, 25, 1], [59, 72, 1], [45, 3, 0], [0, 17, 0],
            0, 40.4, new Dictionary<string, double> { ["Food"] = 67, ["Health"] = 9.6, ["Faith"] = 23.9 },
            new Dictionary<string, double> { ["Low"] = 550, ["Medium"] = 676 }, 25, 127)
        {
            MonthIndex = 12,
            Year = 1930,
            Month = 3,
            PopulationHistory = [new TimePoint(11, 1000), new TimePoint(12, 1010)],
            UnemployedHistory = [new TimePoint(11, 130), new TimePoint(12, 132)],
        },
    };

    private static readonly Finding[] Findings =
    [
        new("a", Severity.Info, Confidence.Probable, "pop") { Category = "Population" },
        new("b", Severity.Info, Confidence.Probable, "house") { Category = "Housing" },
        new("c", Severity.Info, Confidence.Probable, "eco") { Category = "Economy" },
    ];

    // digits only: the thousands separator depends on the machine culture
    private static string Digits(string text) => new(text.Where(char.IsDigit).ToArray());

    [Fact]
    public void Cards_ShowCountsAndTheLookingForAHomeNote()
    {
        var vm = new PopulationViewModel(Snapshot(), Findings);

        Assert.Equal(["pop", "house"], vm.Findings.Select(f => f.Message)); // population and housing findings only
        Assert.Equal("9502701", Digits(vm.AgeSummary)); // 950 adults, 270 children, 1 retired
        Assert.Equal("132", Digits(vm.Unemployed));
        Assert.Equal("48", vm.HomelessFamilies);
        Assert.Equal("127", Digits(vm.HomelessCitizens));
        Assert.Equal("40 / 100", vm.Happiness);
    }

    [Fact]
    public void HappinessCategories_AreSortedWeakestFirst_WithFractionsOnA100Scale()
    {
        var rows = new PopulationViewModel(Snapshot(), Findings).HappinessCategories;

        Assert.Equal(["Health", "Faith", "Food"], rows.Select(r => r.Label));
        Assert.Equal(0.096, rows[0].Fraction, 3);
        Assert.Equal(0.67, rows[2].Fraction, 3);
    }

    [Fact]
    public void Distributions_UseTotalsAsBarBase()
    {
        var vm = new PopulationViewModel(Snapshot(), Findings);

        Assert.Equal(["Uneducated", "High school", "College"], vm.Education.Select(r => r.Label));
        Assert.Equal(850 / 1300.0, vm.Education[0].Fraction, 6);
        Assert.Equal(["Children", "Adults", "Retired"], vm.AgeGroups.Select(r => r.Label));
        Assert.Equal(["Class 1", "Class 2", "Class 3", "Class 4", "Class 5"], vm.Wealth.Select(r => r.Label));
    }

    [Fact]
    public void Homes_ListOnlyTiersWithHomelessOrVacant_RelativeToTheLargest()
    {
        var rows = new PopulationViewModel(Snapshot(), Findings).Homes;

        Assert.Equal(4, rows.Count); // tiers 1 and 2; tier 3 is empty
        Assert.Equal("Tier 1 (cheapest): homeless families", rows[0].Label);
        Assert.Equal(1.0, rows[0].Fraction);           // 45 is the largest value
        Assert.Equal(17 / 45.0, rows[3].Fraction, 6);   // vacant homes, tier 2
    }

    [Fact]
    public void Charts_CarrySeriesAndDateLabels()
    {
        var vm = new PopulationViewModel(Snapshot(), Findings);

        Assert.Equal([1000.0, 1010.0], vm.PopulationSeries[0].Values);
        Assert.Equal(["Unemployed", "Homeless families", "Vacant homes"], vm.JobsHousingSeries.Select(s => s.Name));
        Assert.Equal("Feb 1930", vm.Left);
        Assert.Equal("Mar 1930", vm.Right);
    }

    [Fact]
    public void WithoutDetails_ShowsPlaceholdersInsteadOfFailing()
    {
        var snapshot = Snapshot() with { PopulationData = null };

        var vm = new PopulationViewModel(snapshot, []);

        Assert.Equal("?", vm.Unemployed);
        Assert.Equal("?", vm.HomelessFamilies);
        Assert.Equal("?", vm.Happiness);
        Assert.Empty(vm.Homes);
        Assert.Null(vm.Left);
    }
}
