using Tropico.Localization;
using static Tropico.Analysis.L;

namespace Tropico.Analysis;

/// <summary>
/// Producers whose output storage is (almost) full. The fill level is read directly from the stocks; the consequence (production stops
/// when the output storage is full) is game knowledge, hence Probable.
/// </summary>
public sealed class FullOutputRule : IAnalysisRule
{
    public const int WarningCount = 3;
    public const double WarningShare = 0.25;

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var production = snapshot.Production;

        foreach (var group in production.FullOutputs.GroupBy(o => o.Resource).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var resource = production.Resources.FirstOrDefault(r => r.Resource == group.Key);
            var full = group.Count();
            var share = resource is { Producers: > 0 } ? (double)full / resource.Producers : 0;
            var classes = TextList.Of(group.Select(o => (object?)o.ClassName).Distinct().OrderBy(c => (string?)c, StringComparer.Ordinal));

            var evidence = new List<LocalizedText> { T("evidence.fullOutputs", full, classes) };
            if (resource is not null)
            {
                evidence.Add(T("evidence.producers", resource.Producers));
                evidence.Add(T("evidence.islandStock", resource.IslandStock));
                evidence.Add(T("evidence.exported12", (double)resource.ExportedLast12Months));
            }

            yield return new Finding($"production.full-output.{group.Key}", full >= WarningCount || share >= WarningShare ? Severity.Warning : Severity.Info, Confidence.Probable,
                T("finding.fullOutput", full, Term.Resource(group.Key)))
            {
                Category = "Production",
                SuggestionText = T("suggest.fullOutput"),
                EvidenceTexts = evidence,
            };
        }
    }
}

/// <summary>Resources that are already produced where some deposits have no producer nearby. The producer-to-deposit match is a heuristic.</summary>
public sealed class UntappedDepositsRule : IAnalysisRule
{
    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        foreach (var deposit in snapshot.Production.Deposits.Where(d => d.Producers > 0 && d.Tapped < d.Deposits))
        {
            var free = deposit.Deposits - deposit.Tapped;
            var resource = Term.Resource(deposit.Resource);
            yield return new Finding($"production.untapped-deposits.{deposit.Resource}", Severity.Info, Confidence.Uncertain,
                T("finding.untappedDeposits", free, deposit.Deposits, resource))
            {
                Category = "Production",
                SuggestionText = T("suggest.untappedDeposits", resource),
                EvidenceTexts = [T("evidence.depositProducers", resource, deposit.Producers), T("evidence.depositsInUse", deposit.Tapped)],
            };
        }
    }
}

/// <summary>
/// Nearly every building that has a budget level uses the same one. The meaning of the levels (assumed: a higher level pays more) is not
/// verified, so this is only a lead to check when wages dominate the expenses.
/// </summary>
public sealed class BudgetLevelRule : IAnalysisRule
{
    public const int MinBuildings = 10;
    public const double DominantShare = 0.9;
    public const double WagesShare = 0.5;

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var levels = snapshot.Production.Classes
            .SelectMany(c => c.BudgetLevels.Where(l => l.Key != "-"))
            .GroupBy(l => l.Key)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Value));
        var economy = snapshot.Economy;
        var total = levels.Values.Sum();

        if (total < MinBuildings || economy.YearlyExpenses <= 0) yield break;

        var (level, count) = levels.OrderByDescending(l => l.Value).ThenBy(l => l.Key, StringComparer.Ordinal).First();
        var wages = (double)economy.Wages / economy.YearlyExpenses;
        if ((double)count / total < DominantShare || wages < WagesShare) yield break;

        yield return new Finding("production.budget-uniform", Severity.Info, Confidence.Uncertain,
            T("finding.budgetUniform", count, total, level, wages * 100))
        {
            Category = "Production",
            SuggestionText = T("suggest.budgetUniform"),
            EvidenceTexts = [T("evidence.wages", (double)economy.Wages), T("evidence.totalExpenses", (double)economy.YearlyExpenses)],
        };
    }
}
