using Tropico.Localization;
using static Tropico.Analysis.L;

namespace Tropico.Analysis;

/// <summary>Buildings that are not in the Built state (Damaged, Broken, Rubble...). Building states are read directly from the save.</summary>
public sealed class BuildingConditionRule : IAnalysisRule
{
    private static readonly string[] Healthy = ["Built", IslandSnapshotBuilder.UnknownState];

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var problems = snapshot.Buildings.ByState.Where(s => !Healthy.Contains(s.Key)).OrderBy(s => s.Key).ToList();
        if (problems.Count == 0) yield break;

        var detail = TextList.Of(problems.Select(p => T("fmt.countName", p.Value, new Term("state", p.Key))));
        yield return new Finding("buildings.condition", Severity.Warning, Confidence.Verified,
            T("finding.buildings.condition", problems.Sum(p => p.Value), snapshot.Buildings.Total, detail))
        {
            Category = "Buildings",
            SuggestionText = T("suggest.buildings.condition"),
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

        // sample spacing (game time) is unverified, hence Probable only
        yield return new Finding("treasury.trend", delta < 0 ? Severity.Warning : Severity.Info, Confidence.Probable,
            T(delta < 0 ? "finding.treasury.down" : "finding.treasury.up", Math.Abs(delta), samples, last.Value))
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
            T("finding.balance.negativeMonths", negative, months.Count))
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
            T("finding.unemployment", unemployed, adults.Value, ratio))
        {
            Category = "Population",
            EvidenceTexts = Evidence(snapshot.PopulationInfo),
            SuggestionText = Suggestion(snapshot.PopulationInfo),
        };
    }

    // Per-education split (order assumed: same as the education distribution) and open jobs, when the details are available.
    private static List<LocalizedText> Evidence(PopulationDetails details)
    {
        var lines = new List<LocalizedText>();
        for (var i = 0; i < details.Unemployed.Count && i < PopulationDetails.EducationLevels.Length; i++)
        {
            if (details.Unemployed[i] > 0) lines.Add(T("evidence.unemployed", new Term("education", PopulationDetails.EducationLevels[i]), details.Unemployed[i]));
        }

        if (details.OpenJobs is { } open) lines.Add(T("evidence.openJobs", open));
        return lines;
    }

    private static LocalizedText? Suggestion(PopulationDetails details)
    {
        if (details.OpenJobs is not 0 || details.Unemployed.Count == 0) return null;

        var biggest = details.Unemployed.Select((v, i) => (v, i)).MaxBy(x => x.v);
        return T("suggest.unemployment.noJobs", new Term("education", PopulationDetails.EducationLevels[biggest.i], Lower: true));
    }
}

/// <summary>Homeless families from the last history sample. The series layout is UNVERIFIED (trailing zeros probably omitted).</summary>
public sealed class HomelessFamiliesRule : IAnalysisRule
{
    internal static IEnumerable<LocalizedText> TierEvidence(string labelKey, IReadOnlyList<double> tiers)
    {
        for (var i = 0; i < tiers.Count && i < PopulationDetails.HousingTiers.Length; i++)
        {
            if (tiers[i] > 0) yield return T("evidence.tier", T(labelKey), new Term("tier", PopulationDetails.HousingTiers[i]), tiers[i]);
        }
    }

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var homeless = snapshot.HomelessFamiliesLastSample.Sum();
        if (homeless <= 0) yield break;

        var details = snapshot.PopulationInfo;
        var looking = details.CitizensLookingForHome;

        // With the citizens-looking-for-a-home count from the agents, the homelessness figure is corroborated: Probable.
        yield return new Finding("housing.homeless-families", Severity.Warning, looking is > 0 ? Confidence.Probable : Confidence.Uncertain,
            looking is > 0 ? T("finding.homeless.withCitizens", homeless, looking) : T("finding.homeless", homeless))
        {
            Category = "Housing",
            SuggestionText = T("suggest.homeless"),
            EvidenceTexts = TierEvidence("evidence.homelessFamilies", details.HomelessFamilies)
                .Concat(TierEvidence("evidence.vacantHomes", details.VacantHomes)).ToList(),
        };
    }
}
