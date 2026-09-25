namespace Tropico.Analysis.Tests;

public class PopulationRulesTests
{
    private static IslandSnapshot Snapshot(PopulationDetails? details = null, int? adults = 950, double[]? unemployed = null, double[]? homeless = null) => new(
        "test", "t6-test",
        new BuildingSummary(10, new Dictionary<string, int>(), new Dictionary<string, int> { ["Built"] = 10 }),
        new PopulationSummary(1300, 270, adults, 1, 0),
        100_000, 0, [], [], unemployed ?? [], homeless ?? [])
    {
        PopulationData = details,
    };

    private static PopulationDetails Details(
        double[]? unemployed = null, double[]? homeless = null, double[]? vacant = null, double? openJobs = 0, double? happiness = 60,
        Dictionary<string, double>? categories = null, int? looking = null, double[]? population = null) =>
        new([850, 450, 17], [270, 950, 1], [80, 220, 980, 25, 1], unemployed ?? [0, 0, 0], homeless ?? [0, 0, 0], vacant ?? [0, 0, 0],
            openJobs, happiness, categories ?? new Dictionary<string, double>(), new Dictionary<string, double> { ["Low"] = 550, ["Medium"] = 676, ["High"] = 0 },
            25, looking)
        {
            PopulationHistory = (population ?? []).Select((v, i) => new TimePoint(i, v)).ToList(),
        };

    private static IReadOnlyList<Finding> Run(IslandSnapshot s) => new IslandAnalyzer().Analyze(s).Findings;

    private static Finding? Find(IslandSnapshot s, string code) => Run(s).SingleOrDefault(f => f.Code == code);

    [Fact]
    public void Happiness_LowOverall_ListsWeakestCategoriesAndAGeneralLead()
    {
        var categories = new Dictionary<string, double> { ["Food"] = 67, ["Health"] = 9.6, ["Job"] = 40, ["House"] = 37.4, ["Faith"] = 23.9 };

        var finding = Find(Snapshot(Details(happiness: 40.4, categories: categories)), "population.happiness")!;

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Contains("40 out of 100", finding.Message);
        Assert.Equal(["Health: 9.6", "Faith: 23.9", "House: 37.4"], finding.Evidence.Take(3));
        Assert.Contains("Low happiness: 550 citizens", finding.Evidence);
        Assert.DoesNotContain("High happiness", string.Join(";", finding.Evidence)); // zero counts are not listed
        Assert.Contains("health", finding.Suggestion);
        Assert.Contains("clinics", finding.Suggestion);
    }

    [Fact]
    public void Happiness_Thresholds()
    {
        Assert.Equal(Severity.Warning, Find(Snapshot(Details(happiness: 30)), "population.happiness")!.Severity);
        Assert.Null(Find(Snapshot(Details(happiness: 50)), "population.happiness"));
        Assert.Null(Find(Snapshot(Details(happiness: null)), "population.happiness"));
        Assert.Null(Find(Snapshot(), "population.happiness")); // no details at all
    }

    [Fact]
    public void PopulationTrend_UsesTheLast12Months()
    {
        var falling = Enumerable.Range(0, 20).Select(i => 1000.0 - i * 5).ToArray();

        var finding = Find(Snapshot(Details(population: falling)), "population.trend")!;

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("fell by 60", finding.Message); // 12 months x 5
        Assert.Contains("last 12 months", finding.Message);
        Assert.NotNull(finding.Suggestion);

        var growing = Find(Snapshot(Details(population: [100, 110])), "population.trend")!;
        Assert.Equal(Severity.Info, growing.Severity);
        Assert.Contains("grew by 10 (10 %)", growing.Message.Replace(" ", " "));
        Assert.Null(growing.Suggestion);

        Assert.Null(Find(Snapshot(Details(population: [100])), "population.trend"));
    }

    [Fact]
    public void Homeless_WithCitizensLookingForAHome_IsProbableAndListsTiers()
    {
        var snapshot = Snapshot(Details(homeless: [45, 3, 0], vacant: [0, 17, 0], looking: 127), homeless: [45, 3]);

        var finding = Find(snapshot, "housing.homeless-families")!;

        Assert.Equal(Confidence.Probable, finding.Confidence);
        Assert.Contains("48 families", finding.Message);
        Assert.Contains("127 citizens", finding.Message);
        Assert.Equal(["Homeless families, Tier 1 (cheapest): 45", "Homeless families, Tier 2: 3", "Vacant homes, Tier 2: 17"], finding.Evidence);
    }

    [Fact]
    public void Homeless_WithoutAgentCorroboration_StaysUncertain()
    {
        var finding = Find(Snapshot(homeless: [10]), "housing.homeless-families")!;

        Assert.Equal(Confidence.Uncertain, finding.Confidence);
        Assert.DoesNotContain("citizens", finding.Message);
    }

    [Fact]
    public void TierMismatch_VacantHomesOnlyInOtherTiers()
    {
        var finding = Find(Snapshot(Details(homeless: [45, 3, 0], vacant: [0, 17, 0])), "housing.tier-mismatch")!;

        Assert.Equal(Confidence.Uncertain, finding.Confidence);
        Assert.Contains("17 homes are vacant", finding.Message);
        Assert.Contains("Tier 1 (cheapest)", finding.Message);
        Assert.DoesNotContain("Tier 2", finding.Message);
    }

    [Fact]
    public void TierMismatch_IsSilentWhenTiersMatchOrNothingIsVacant()
    {
        Assert.Null(Find(Snapshot(Details(homeless: [5, 0, 0], vacant: [3, 0, 0])), "housing.tier-mismatch"));
        Assert.Null(Find(Snapshot(Details(homeless: [5, 0, 0], vacant: [0, 0, 0])), "housing.tier-mismatch"));
        Assert.Null(Find(Snapshot(Details(homeless: [0, 0, 0], vacant: [0, 9, 0])), "housing.tier-mismatch"));
    }

    [Fact]
    public void Unemployment_WithDetails_SplitsByEducationAndPointsAtMissingJobs()
    {
        var snapshot = Snapshot(Details(unemployed: [59, 72, 1], openJobs: 0), unemployed: [59, 72, 1]);

        var finding = Find(snapshot, "population.unemployment")!;

        Assert.Equal(Confidence.Uncertain, finding.Confidence); // the education order of the series is assumed
        Assert.Equal(["Uneducated: 59", "High school: 72", "College: 1", "Open jobs: 0"], finding.Evidence);
        Assert.Contains("no open jobs", finding.Suggestion);
        Assert.Contains("high school", finding.Suggestion);
    }

    [Fact]
    public void Unemployment_WithOpenJobs_HasNoJobSuggestion()
    {
        var finding = Find(Snapshot(Details(unemployed: [30, 0, 0], openJobs: 12), unemployed: [30, 0, 0]), "population.unemployment")!;

        Assert.Null(finding.Suggestion);
        Assert.Contains("Open jobs: 12", finding.Evidence);
    }
}
