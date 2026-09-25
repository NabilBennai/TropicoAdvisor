using Tropico.Localization;
using static Tropico.Analysis.L;

namespace Tropico.Analysis;

/// <summary>
/// How many months of spending the treasury covers. Yearly expenses are a sum of the last-year buckets whose semantics are only
/// partly verified, so the figure is an order of magnitude, hence Probable.
/// </summary>
public sealed class TreasuryRunwayRule : IAnalysisRule
{
    public const double LowMonths = 3;      // heuristic thresholds, not game rules
    public const double IdleMonths = 24;

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var expenses = snapshot.Economy.YearlyExpenses;
        if (snapshot.Treasury is not { } treasury || expenses <= 0) yield break;

        var monthly = expenses / 12.0;
        var months = treasury / monthly;
        LocalizedText[] evidence = [T("evidence.treasury", treasury), T("evidence.expensesLastYear", (double)expenses, monthly)];

        if (months < LowMonths)
        {
            yield return new Finding("economy.runway-low", months < 1 ? Severity.Critical : Severity.Warning, Confidence.Probable,
                T("finding.runway.low", months))
            {
                Category = "Economy",
                SuggestionText = T("suggest.runway.low"),
                EvidenceTexts = evidence,
            };
        }
        else if (months > IdleMonths)
        {
            yield return new Finding("economy.runway-idle", Severity.Info, Confidence.Probable,
                T("finding.runway.idle", months))
            {
                Category = "Economy",
                SuggestionText = T("suggest.runway.idle"),
                EvidenceTexts = evidence,
            };
        }
    }
}

/// <summary>Wages as a share of last year's expenses.</summary>
public sealed class WageBurdenRule : IAnalysisRule
{
    public const double InfoShare = 0.60;
    public const double WarningShare = 0.80;

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var economy = snapshot.Economy;
        if (economy.YearlyExpenses <= 0) yield break;

        var share = (double)economy.Wages / economy.YearlyExpenses;
        if (share < InfoShare) yield break;

        yield return new Finding("economy.wage-burden", share >= WarningShare ? Severity.Warning : Severity.Info, Confidence.Probable,
            T("finding.wageBurden", share * 100))
        {
            Category = "Economy",
            SuggestionText = T("suggest.wageBurden"),
            EvidenceTexts = [T("evidence.wages", (double)economy.Wages), T("evidence.upkeep", (double)economy.Upkeep), T("evidence.totalExpenses", (double)economy.YearlyExpenses)],
        };
    }
}

/// <summary>
/// The building classes that cost the most (wages + upkeep) without matching direct income.
/// Producers have no direct income (their output is sold through exports), so this lists cost centers, NOT loss-making buildings.
/// </summary>
public sealed class CostCenterRule : IAnalysisRule
{
    public const int Top = 3;

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var centers = snapshot.Economy.ClassCosts.Where(c => c.Cost > c.FeesAndRents).OrderByDescending(c => c.Cost - c.FeesAndRents).Take(Top).ToList();
        var totalCost = snapshot.Economy.ClassCosts.Sum(c => c.Cost);
        if (centers.Count == 0 || totalCost <= 0) yield break;

        var evidence = centers.Select(c => c.Instances > 0
            ? T("evidence.costCenter", c.ClassName, (double)c.Cost, (double)c.Cost / totalCost * 100, c.Instances, (double)c.Cost / c.Instances)
            : T("evidence.costCenter.noPer", c.ClassName, (double)c.Cost, (double)c.Cost / totalCost * 100, c.Instances)).ToList();

        yield return new Finding("economy.cost-centers", Severity.Info, Confidence.Probable,
            T("finding.costCenters", TextList.Of(centers.Select(c => (object?)c.ClassName))))
        {
            Category = "Economy",
            SuggestionText = T("suggest.costCenters"),
            EvidenceTexts = evidence,
        };
    }
}

/// <summary>Imports as a share of last year's expenses.</summary>
public sealed class ImportDependenceRule : IAnalysisRule
{
    public const double InfoShare = 0.15;
    public const double WarningShare = 0.30;

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var economy = snapshot.Economy;
        if (economy.YearlyExpenses <= 0) yield break;

        var share = (double)economy.Imports / economy.YearlyExpenses;
        if (share < InfoShare) yield break;

        yield return new Finding("economy.import-dependence", share >= WarningShare ? Severity.Warning : Severity.Info, Confidence.Probable,
            T("finding.importDependence", share * 100))
        {
            Category = "Trade",
            SuggestionText = T("suggest.importDependence"),
            EvidenceTexts = [T("evidence.imports", (double)economy.Imports), T("evidence.totalExpenses", (double)economy.YearlyExpenses)],
        };
    }
}

/// <summary>Share of the best export resource in last year's export revenue.</summary>
public sealed class ExportConcentrationRule : IAnalysisRule
{
    public const double InfoShare = 0.50;
    public const double WarningShare = 0.70;

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var economy = snapshot.Economy;
        var total = economy.ExportRevenueByResource.Values.Sum();
        if (total <= 0) yield break;

        var top = economy.ExportRevenueByResource.OrderByDescending(e => e.Value).ThenBy(e => e.Key, StringComparer.Ordinal).Take(3).ToList();
        var share = (double)top[0].Value / total;
        if (share < InfoShare) yield break;

        yield return new Finding("trade.export-concentration", share >= WarningShare ? Severity.Warning : Severity.Info, Confidence.Probable,
            T("finding.exportConcentration", Term.Resource(ResourceName(top[0].Key)), share * 100))
        {
            Category = "Trade",
            SuggestionText = T("suggest.exportConcentration"),
            EvidenceTexts = top.Select(e => T("evidence.exportShare", Term.Resource(ResourceName(e.Key)), (double)e.Value, (double)e.Value / total * 100)).ToList(),
        };
    }

    // "ET6ResourceType::Gold" -> "Gold"
    private static string ResourceName(string name)
    {
        var separator = name.LastIndexOf("::", StringComparison.Ordinal);
        return separator < 0 ? name : name[(separator + 2)..];
    }
}

/// <summary>
/// Large stocks of a resource that was not exported at all during the last 12 months although partners offer export routes for it.
/// The stock may be kept for local production, so this is a lead to check, not a fault.
/// </summary>
public sealed class IdleStockRule : IAnalysisRule
{
    public const double MinStock = 500;
    public const double MinValue = 5_000;
    public const int Top = 3;

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var idle = snapshot.Economy.Goods
            .Where(g => g.StockTotal >= MinStock && g.ExportedLast12Months == 0 && g.ExportOffers > 0 && g.StockValue >= MinValue)
            .OrderByDescending(g => g.StockValue)
            .Take(Top);

        foreach (var good in idle)
        {
            yield return new Finding($"trade.idle-stock.{good.Resource}", Severity.Info, Confidence.Probable,
                T("finding.idleStock", good.StockTotal, Term.Resource(good.Resource), good.StockValue))
            {
                Category = "Trade",
                SuggestionText = T("suggest.idleStock", good.ExportOffers, TextList.Of(good.ExportPartners.Select(p => (object?)p))),
                EvidenceTexts =
                [
                    T("evidence.stock", good.StockTotal),
                    T("evidence.price", good.CurrentPrice ?? 0),
                    T("evidence.exportRoutes", good.ExportOffers),
                ],
            };
        }
    }
}

/// <summary>
/// A resource whose last price sample is well above its earlier average while stock is available and an export route exists.
/// UNCERTAIN: the order of the price history (last = newest) is assumed, not verified.
/// </summary>
public sealed class PriceOpportunityRule : IAnalysisRule
{
    public const double MinRatio = 1.10;
    public const double MinStock = 100;
    public const int Top = 3;

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var opportunities = snapshot.Economy.Goods
            .Where(g => g.PriceRatio >= MinRatio && g.StockTotal >= MinStock && g.ExportOffers > 0)
            .OrderByDescending(g => g.StockTotal * (g.CurrentPrice!.Value - g.AveragePrice!.Value))
            .Take(Top);

        foreach (var good in opportunities)
        {
            yield return new Finding($"trade.price-high.{good.Resource}", Severity.Info, Confidence.Uncertain,
                T("finding.priceHigh", Term.Resource(good.Resource), (good.PriceRatio!.Value - 1) * 100, good.StockTotal))
            {
                Category = "Trade",
                SuggestionText = T("suggest.priceHigh"),
                EvidenceTexts = [T("evidence.priceNow", good.CurrentPrice!.Value), T("evidence.priceBefore", good.AveragePrice!.Value), T("evidence.stock", good.StockTotal)],
            };
        }
    }
}
