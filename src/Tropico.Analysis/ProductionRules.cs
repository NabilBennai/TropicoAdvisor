using System.Globalization;

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
            var classes = string.Join(", ", group.Select(o => o.ClassName).Distinct().Order());

            var evidence = new List<string> { $"Full output stocks: {full} ({classes})" };
            if (resource is not null)
            {
                evidence.Add($"Producers: {resource.Producers}");
                evidence.Add($"Island stock: {Fmt.N0(resource.IslandStock)}");
                evidence.Add($"Exported in the last 12 months: {Fmt.N0(resource.ExportedLast12Months)}");
            }

            yield return new Finding($"production.full-output.{group.Key}", full >= WarningCount || share >= WarningShare ? Severity.Warning : Severity.Info, Confidence.Probable,
                $"{full} {group.Key} producer(s) have a full output storage.")
            {
                Category = "Production",
                Suggestion = "Production stops when the storage is full: sell more of it (see Trade), add teamsters or storage, or build fewer producers.",
                Evidence = evidence,
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
            yield return new Finding($"production.untapped-deposits.{deposit.Resource}", Severity.Info, Confidence.Uncertain,
                $"{free} of {deposit.Deposits} {deposit.Resource} deposits appear to have no producer nearby.")
            {
                Category = "Production",
                Suggestion = $"There is room to expand {deposit.Resource} production if you want more of it (the match between producers and deposits is estimated by distance).",
                Evidence = [$"{deposit.Resource} producers: {deposit.Producers}", $"Deposits in use (estimated): {deposit.Tapped}"],
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
            string.Create(CultureInfo.InvariantCulture, $"{count:N0} of {total:N0} workplaces use the same budget level ({level}) while wages are {Fmt.Percent(wages)} of expenses."))
        {
            Category = "Production",
            Suggestion = "If this is the most generous level, lowering it on low-value workplaces would cut the wage bill (the level meaning is not verified).",
            Evidence = [$"Wages: {Fmt.N0(economy.Wages)}", $"Total expenses: {Fmt.N0(economy.YearlyExpenses)}"],
        };
    }
}
