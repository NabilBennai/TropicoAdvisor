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
            $"{problems.Sum(p => p.Value)} of {snapshot.Buildings.Total} buildings are not in good condition ({detail}).");
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
            string.Create(CultureInfo.InvariantCulture, $"Treasury {direction} by {Math.Abs(delta):N0} over the last {samples} samples (now {last.Value:N0})."));
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
            $"{negative} of the last {months.Count} monthly balances are negative.");
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
                $"About {unemployed:N0} unemployed for {adults:N0} adults ({ratio:P0}); the unemployment series meaning is unverified."));
    }
}

/// <summary>Homeless families from the last history sample. The series layout is UNVERIFIED (trailing zeros probably omitted).</summary>
public sealed class HomelessFamiliesRule : IAnalysisRule
{
    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var homeless = snapshot.HomelessFamiliesLastSample.Sum();
        if (homeless <= 0) yield break;

        yield return new Finding("housing.homeless-families", Severity.Warning, Confidence.Uncertain,
            string.Create(CultureInfo.InvariantCulture, $"About {homeless:N0} families appear to be homeless; the series layout is unverified."));
    }
}
