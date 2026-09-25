using Tropico.Localization;
using static Tropico.Analysis.L;

namespace Tropico.Analysis;

/// <summary>Overall happiness and its weakest categories. The overall value is verified against the agents; the thresholds are heuristics.</summary>
public sealed class HappinessRule : IAnalysisRule
{
    public const double InfoBelow = 50;     // heuristic thresholds on the 0-100 scale
    public const double WarningBelow = 35;
    public const int WeakestShown = 3;

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var details = snapshot.PopulationInfo;
        if (details.HappinessOverall is not { } overall || overall >= InfoBelow) yield break;

        var weakest = details.HappinessByCategory.OrderBy(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal).Take(WeakestShown).ToList();
        var evidence = weakest.Select(c => T("evidence.happinessCategory", new Term("happiness", c.Key), c.Value)).ToList();
        foreach (var (level, count) in details.HappinessLevels.Where(l => l.Value > 0)) evidence.Add(T("evidence.happinessLevel", new Term("level", level), count));

        yield return new Finding("population.happiness", overall < WarningBelow ? Severity.Warning : Severity.Info, Confidence.Probable,
            T("finding.happiness", overall))
        {
            Category = "Population",
            // General leads only, per happiness category (catalog keys lead.happiness.*).
            SuggestionText = weakest.Count > 0
                ? T("suggest.happiness", new Term("happiness", weakest[0].Key, Lower: true), T("lead.happiness." + weakest[0].Key))
                : null,
            EvidenceTexts = evidence,
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
            T(delta < 0 ? "finding.population.fell" : "finding.population.grew", Math.Abs(delta), Math.Abs(ratio), samples, history[^1].Value))
        {
            Category = "Population",
            SuggestionText = delta < 0 ? T("suggest.populationFalling") : null,
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
            T("finding.tierMismatch", details.TotalVacantHomes, TextList.Of(mismatched.Select(i => (object?)new Term("tier", PopulationDetails.HousingTiers[i])))))
        {
            Category = "Housing",
            SuggestionText = T("suggest.tierMismatch"),
            EvidenceTexts = HomelessFamiliesRule.TierEvidence("evidence.homelessFamilies", details.HomelessFamilies)
                .Concat(HomelessFamiliesRule.TierEvidence("evidence.vacantHomes", details.VacantHomes)).ToList(),
        };
    }
}
