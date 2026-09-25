using System.Globalization;

namespace Tropico.Analysis;

internal static class Fmt
{
    public static string N0(double value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string D1(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    public static string D2(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Percent(double ratio) => (ratio * 100).ToString("0", CultureInfo.InvariantCulture) + " %";
}

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
        var evidence = new[] { $"Treasury: {Fmt.N0(treasury)}", $"Expenses last year: {Fmt.N0(expenses)} (about {Fmt.N0(monthly)} per month)" };

        if (months < LowMonths)
        {
            yield return new Finding("economy.runway-low", months < 1 ? Severity.Critical : Severity.Warning, Confidence.Probable,
                $"The treasury covers only about {Fmt.D1(months)} months of spending.")
            {
                Category = "Economy",
                Suggestion = "Raise income (export stocked goods, open trade routes) or cut costs (wages, idle workplaces) before the balance turns negative.",
                Evidence = evidence,
            };
        }
        else if (months > IdleMonths)
        {
            yield return new Finding("economy.runway-idle", Severity.Info, Confidence.Probable,
                $"The treasury covers about {Fmt.N0(months)} months of spending: a large part of it is idle.")
            {
                Category = "Economy",
                Suggestion = "Consider investing in growth (housing, production chains, education, tourism) or in what your citizens and factions ask for.",
                Evidence = evidence,
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
            $"Wages are {Fmt.Percent(share)} of expenses.")
        {
            Category = "Economy",
            Suggestion = "Check the wage level of the costliest workplaces and whether some are overstaffed; extra workplaces should produce or earn more than they cost.",
            Evidence = [$"Wages: {Fmt.N0(economy.Wages)}", $"Upkeep: {Fmt.N0(economy.Upkeep)}", $"Total expenses: {Fmt.N0(economy.YearlyExpenses)}"],
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

        var evidence = centers.Select(c =>
        {
            var perBuilding = c.Instances > 0 ? $", {Fmt.N0((double)c.Cost / c.Instances)} per building" : "";
            return $"{c.ClassName}: {Fmt.N0(c.Cost)} ({Fmt.Percent((double)c.Cost / totalCost)} of costs, {c.Instances} buildings{perBuilding})";
        }).ToList();

        yield return new Finding("economy.cost-centers", Severity.Info, Confidence.Probable,
            $"Costliest building classes last year: {string.Join(", ", centers.Select(c => c.ClassName))}.")
        {
            Category = "Economy",
            Suggestion = "These have no direct income: make sure their output is sold or used, and that every building of the class is actually productive.",
            Evidence = evidence,
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
            $"Imports are {Fmt.Percent(share)} of expenses.")
        {
            Category = "Trade",
            Suggestion = "Check whether the imported goods can be produced on the island, or bought through cheaper routes.",
            Evidence = [$"Imports: {Fmt.N0(economy.Imports)}", $"Total expenses: {Fmt.N0(economy.YearlyExpenses)}"],
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
            $"{ResourceName(top[0].Key)} makes {Fmt.Percent(share)} of export revenue.")
        {
            Category = "Trade",
            Suggestion = "A single dominant export exposes the income to price swings and route changes: develop a second or third export.",
            Evidence = top.Select(e => $"{ResourceName(e.Key)}: {Fmt.N0(e.Value)} ({Fmt.Percent((double)e.Value / total)})").ToList(),
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
                $"{Fmt.N0(good.StockTotal)} {good.Resource} in stock, none exported in the last 12 months (worth about {Fmt.N0(good.StockValue)}).")
            {
                Category = "Trade",
                Suggestion = $"If it is not needed for local production, sell it: {good.ExportOffers} export route(s) are offered by {string.Join(", ", good.ExportPartners)}.",
                Evidence =
                [
                    $"Stock: {Fmt.N0(good.StockTotal)}",
                    $"Price: {Fmt.D2(good.CurrentPrice ?? 0)}",
                    $"Export routes offered: {good.ExportOffers}",
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
                $"{good.Resource} sells {Fmt.Percent(good.PriceRatio!.Value - 1)} above its average price and {Fmt.N0(good.StockTotal)} are in stock.")
            {
                Category = "Trade",
                Suggestion = "A good moment to sell part of the stock, if the price history is ordered oldest to newest as assumed.",
                Evidence = [$"Price now: {Fmt.D2(good.CurrentPrice!.Value)}", $"Average before: {Fmt.D2(good.AveragePrice!.Value)}", $"Stock: {Fmt.N0(good.StockTotal)}"],
            };
        }
    }
}
