using System.Globalization;

namespace Tropico.Analysis;

/// <summary>Buildings that are not in the Built state (Damaged, Broken, Rubble...). Building states are read directly from the save.</summary>
public sealed class BuildingConditionRule : IAnalysisRule
{
    private static readonly string[] Healthy = ["Built", IslandSnapshotBuilder.UnknownState];

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var problems = snapshot.Buildings.ByState.Where(s => !Healthy.Contains(s.Key)).OrderBy(s => s.Key).ToList();
        if (problems.Count == 0) yield break;

        var detail = string.Join(", ", problems.Select(p => $"{p.Value} {p.Key}"));
        yield return new Finding("buildings.condition", Severity.Warning, Confidence.Verified,
            $"{problems.Sum(p => p.Value)} of {snapshot.Buildings.Total} buildings are not in good condition ({detail}).")
        {
            Category = "Buildings",
            Suggestion = "Repair or rebuild them: damaged and broken buildings stop working, rubble frees the space.",
        };
    }
}

/// <summary>Treasury change between the last history sample and the sample <see cref="Window"/> samples earlier.</summary>
public sealed class TreasuryTrendRule : IAnalysisRule
{
    public const int Window = 12;

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var history = snapshot.TreasuryHistory;
        if (history.Count < 2) yield break;

        var referenceIndex = Math.Max(0, history.Count - 1 - Window);
        var last = history[^1];
        var delta = last.Value - history[referenceIndex].Value;
        var samples = history.Count - 1 - referenceIndex;
        var direction = delta < 0 ? "decreased" : "increased";

        // sample spacing (game time) is unverified, hence Probable only
        yield return new Finding("treasury.trend", delta < 0 ? Severity.Warning : Severity.Info, Confidence.Probable,
            string.Create(CultureInfo.InvariantCulture, $"Treasury {direction} by {Math.Abs(delta):N0} over the last {samples} samples (now {last.Value:N0})."))
        {
            Category = "Economy",
        };
    }
}

/// <summary>Months with a negative balance among the stored monthly balances.</summary>
public sealed class MonthlyBalanceRule : IAnalysisRule
{
    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var months = snapshot.MonthlyBalance;
        var negative = months.Count(m => m < 0);
        if (negative == 0) yield break;

        yield return new Finding("balance.negative-months", negative * 2 >= months.Count ? Severity.Warning : Severity.Info, Confidence.Probable,
            $"{negative} of the last {months.Count} monthly balances are negative.")
        {
            Category = "Economy",
        };
    }
}

/// <summary>Unemployed share of adults. The unemployed series is UNVERIFIED (assumed to be three education levels), so confidence is low.</summary>
public sealed class UnemploymentRule : IAnalysisRule
{
    public const double WarningRatio = 0.10; // heuristic threshold, not a game rule

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var adults = snapshot.Population.Adults;
        if (adults is not > 0 || snapshot.UnemployedLastSample.Count == 0) yield break;

        var unemployed = snapshot.UnemployedLastSample.Sum();
        var ratio = unemployed / adults.Value;

        yield return new Finding("population.unemployment",
            ratio >= WarningRatio ? Severity.Warning : Severity.Info, Confidence.Uncertain,
            string.Create(CultureInfo.InvariantCulture,
                $"About {unemployed:N0} unemployed for {adults:N0} adults ({ratio:P0}); the unemployment series meaning is unverified."))
        {
            Category = "Population",
            Evidence = Evidence(snapshot.PopulationInfo),
            Suggestion = Suggestion(snapshot.PopulationInfo),
        };
    }

    // Per-education split (order assumed: same as the education distribution) and open jobs, when the details are available.
    private static List<string> Evidence(PopulationDetails details)
    {
        var lines = new List<string>();
        for (var i = 0; i < details.Unemployed.Count && i < PopulationDetails.EducationLevels.Length; i++)
        {
            if (details.Unemployed[i] > 0) lines.Add($"{PopulationDetails.EducationLevels[i]}: {details.Unemployed[i]:N0}");
        }

        if (details.OpenJobs is { } open) lines.Add($"Open jobs: {open:N0}");
        return lines;
    }

    private static string? Suggestion(PopulationDetails details)
    {
        if (details.OpenJobs is not 0 || details.Unemployed.Count == 0) return null;

        var biggest = details.Unemployed.Select((v, i) => (v, i)).MaxBy(x => x.v);
        return $"There are no open jobs: create workplaces, especially ones that fit the largest jobless group ({PopulationDetails.EducationLevels[biggest.i].ToLowerInvariant()}).";
    }
}

/// <summary>Homeless families from the last history sample. The series layout is UNVERIFIED (trailing zeros probably omitted).</summary>
public sealed class HomelessFamiliesRule : IAnalysisRule
{
    internal static IEnumerable<string> TierEvidence(string label, IReadOnlyList<double> tiers)
    {
        for (var i = 0; i < tiers.Count && i < PopulationDetails.HousingTiers.Length; i++)
        {
            if (tiers[i] > 0) yield return $"{label}, {PopulationDetails.HousingTiers[i]}: {tiers[i]:N0}";
        }
    }

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var homeless = snapshot.HomelessFamiliesLastSample.Sum();
        if (homeless <= 0) yield break;

        var details = snapshot.PopulationInfo;
        var looking = details.CitizensLookingForHome;
        var citizens = looking is > 0 ? string.Create(CultureInfo.InvariantCulture, $" (about {looking:N0} citizens are looking for a home)") : "";

        // With the citizens-looking-for-a-home count from the agents, the homelessness figure is corroborated: Probable.
        yield return new Finding("housing.homeless-families", Severity.Warning, looking is > 0 ? Confidence.Probable : Confidence.Uncertain,
            string.Create(CultureInfo.InvariantCulture, $"About {homeless:N0} families appear to be homeless{citizens}."))
        {
            Category = "Housing",
            Suggestion = "Build more housing, and check that it matches the wealth of the families that need it.",
            Evidence = TierEvidence("Homeless families", details.HomelessFamilies).Concat(TierEvidence("Vacant homes", details.VacantHomes)).ToList(),
        };
    }
}
