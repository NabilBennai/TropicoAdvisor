using Tropico.SaveParser;

namespace Tropico.Analysis.Tests;

public class PopulationSnapshotTests
{
    private static readonly Lazy<T6SaveFile> Urss = new(() => T6SaveReader.Read(IslandSnapshotBuilderTests.FindSample()));

    [Fact]
    public void Build_ReferenceSave_ExposesPopulationDetails()
    {
        var snapshot = IslandSnapshotBuilder.Build(Urss.Value);
        var details = snapshot.PopulationInfo;

        Assert.Equal([851.0, 454.0, 17.0], details.Education);                 // verified against the living agents (852, 454, 17)
        Assert.Equal([273.0, 952.0, 1.0], details.AgeGroups);                  // children, adults, retired counters
        Assert.Equal(1322, details.Wealth.Sum());
        Assert.Equal(5, details.Wealth.Count);
        Assert.Equal([59.0, 72.0, 1.0], details.Unemployed);
        Assert.Equal([45.0, 3.0, 0.0], details.HomelessFamilies);               // trailing zero padded
        Assert.Equal([0.0, 17.0, 0.0], details.VacantHomes);
        Assert.Equal(0, details.OpenJobs);
        Assert.Equal(127, details.CitizensLookingForHome);
        Assert.Equal(25, details.AverageAge);

        Assert.Equal(40.38, details.HappinessOverall!.Value, 2);                // equals the mean of the agents' overall happiness (40.37)
        Assert.Equal(9.59, details.HappinessByCategory["Health"], 2);
        Assert.Equal(67.28, details.HappinessByCategory["Food"], 2);
        Assert.Equal(8, details.HappinessByCategory.Count);
        Assert.Equal(550, details.HappinessLevels["Low"]);
        Assert.Equal(676, details.HappinessLevels["Medium"]);

        Assert.Equal(61, details.PopulationHistory.Count);
        Assert.Equal(1313, details.PopulationHistory[^1].Value);
        Assert.Equal((1929, 9), details.DateOf(details.PopulationHistory[0].X));
        Assert.Equal(
            new PopulationSummary(1313, 273, 952, 1, 21) { Soldiers = 4, Voters = 773, NativeTropicans = 519, Immigrants = 794 },
            snapshot.Population);
    }

    [Fact]
    public void Analyze_ReferenceSave_ProducesPopulationAndHousingSuggestions()
    {
        var findings = new IslandAnalyzer().Analyze(Urss.Value).Findings;

        var homeless = Assert.Single(findings, f => f.Code == "housing.homeless-families");
        Assert.Contains("127 citizens", homeless.Message);
        Assert.Contains(findings, f => f.Code == "housing.tier-mismatch");
        Assert.Contains(findings, f => f.Code == "population.trend");
        Assert.Contains("healthcare", Assert.Single(findings, f => f.Code == "population.happiness").Suggestion);
    }
}
