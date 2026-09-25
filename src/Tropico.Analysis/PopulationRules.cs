using System.Globalization;

namespace Tropico.Analysis;

/// <summary>Overall happiness and its weakest categories. The overall value is verified against the agents; the thresholds are heuristics.</summary>
public sealed class HappinessRule : IAnalysisRule
{
    public const double InfoBelow = 50;     // heuristic thresholds on the 0-100 scale
    public const double WarningBelow = 35;
    public const int WeakestShown = 3;

    // General leads only, per happiness category.
    private static readonly Dictionary<string, string> Leads = new()
    {
        ["Food"] = "food production and distribution (farms, ranches, groceries)",
        ["Health"] = "healthcare (clinics, hospitals)",
        ["Job"] = "job availability and wages",
        ["House"] = "housing quantity and quality",
        ["Faith"] = "religion (chapels, churches)",
        ["Fun"] = "entertainment (taverns, circuses, cinemas)",
        ["Liberty"] = "freedom-related edicts and constitution choices",
        ["Safety"] = "safety (police, army)",
    };

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var details = snapshot.PopulationInfo;
        if (details.HappinessOverall is not { } overall || overall >= InfoBelow) yield break;

        var weakest = details.HappinessByCategory.OrderBy(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal).Take(WeakestShown).ToList();
        var evidence = weakest.Select(c => $"{c.Key}: {c.Value.ToString("0.0", CultureInfo.InvariantCulture)}").ToList();
        foreach (var (level, count) in details.HappinessLevels.Where(l => l.Value > 0)) evidence.Add($"{level} happiness: {count:N0} citizens");

        yield return new Finding("population.happiness", overall < WarningBelow ? Severity.Warning : Severity.Info, Confidence.Probable,
            string.Create(CultureInfo.InvariantCulture, $"Overall happiness is {overall:0} out of 100."))
        {
            Category = "Population",
            Suggestion = weakest.Count > 0 && Leads.TryGetValue(weakest[0].Key, out var lead)
                ? $"The weakest category is {weakest[0].Key.ToLowerInvariant()}: look at {lead}."
                : null,
            Evidence = evidence,
        };
    }
}

/// <summary>Population change over the last 12 monthly samples.</summary>
public sealed class PopulationTrendRule : IAnalysisRule
{
    public const int Window = 12;

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var history = snapshot.PopulationInfo.PopulationHistory;
        if (history.Count < 2) yield break;

        var referenceIndex = Math.Max(0, history.Count - 1 - Window);
        var delta = history[^1].Value - history[referenceIndex].Value;
        var samples = history.Count - 1 - referenceIndex;
        var ratio = history[referenceIndex].Value > 0 ? delta / history[referenceIndex].Value : 0;

        yield return new Finding("population.trend", delta < 0 ? Severity.Warning : Severity.Info, Confidence.Probable,
            string.Create(CultureInfo.InvariantCulture,
                $"Population {(delta < 0 ? "fell" : "grew")} by {Math.Abs(delta):N0} ({Math.Abs(ratio):P0}) over the last {samples} months (now {history[^1].Value:N0})."))
        {
            Category = "Population",
            Suggestion = delta < 0 ? "Check housing, food and healthcare: emigration and deaths drive a shrinking population." : null,
        };
    }
}

/// <summary>
/// Vacant homes exist while families are homeless, but not in the tier where the families are.
/// UNCERTAIN: the meaning of the three housing tiers (assumed cheapest first) is not verified.
/// </summary>
public sealed class HousingTierMismatchRule : IAnalysisRule
{
    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var details = snapshot.PopulationInfo;
        var mismatched = Enumerable.Range(0, Math.Min(details.HomelessFamilies.Count, details.VacantHomes.Count))
            .Where(i => details.HomelessFamilies[i] > 0 && details.VacantHomes[i] == 0)
            .ToList();
        if (mismatched.Count == 0 || details.TotalVacantHomes <= 0) yield break;

        yield return new Finding("housing.tier-mismatch", Severity.Info, Confidence.Uncertain,
            string.Create(CultureInfo.InvariantCulture,
                $"{details.TotalVacantHomes:N0} homes are vacant, but none in the tier where families are homeless ({string.Join(", ", mismatched.Select(i => PopulationDetails.HousingTiers[i]))})."))
        {
            Category = "Housing",
            Suggestion = "Build housing of the tier where families are homeless, rather than more of the tier that is already vacant.",
            Evidence = HomelessFamiliesRule.TierEvidence("Homeless families", details.HomelessFamilies)
                .Concat(HomelessFamiliesRule.TierEvidence("Vacant homes", details.VacantHomes)).ToList(),
        };
    }
}
