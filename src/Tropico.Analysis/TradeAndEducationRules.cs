using Tropico.Localization;
using static Tropico.Analysis.L;

namespace Tropico.Analysis;

/// <summary>Last year's imports exceeded the export revenue.</summary>
public sealed class TradeBalanceRule : IAnalysisRule
{
    public const double MinImports = 5_000;
    public const double WarningRatio = 1.5;

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var economy = snapshot.Economy;
        if (economy.Imports < MinImports || economy.Imports <= economy.ExportRevenue) yield break;

        var severity = economy.Imports >= economy.ExportRevenue * WarningRatio ? Severity.Warning : Severity.Info;
        yield return new Finding("trade.deficit", severity, Confidence.Probable,
            T("finding.tradeDeficit", (double)economy.Imports, (double)economy.ExportRevenue, (double)(economy.Imports - economy.ExportRevenue)))
        {
            Category = "Trade",
            SuggestionText = T("suggest.tradeDeficit"),
            EvidenceTexts = [T("evidence.imports", (double)economy.Imports), T("evidence.exportRevenue", (double)economy.ExportRevenue)],
        };
    }
}

/// <summary>
/// A resource that is exported and stocked while its price is well below its average. UNCERTAIN: the average
/// depends on the price history order, which is assumed, not verified.
/// </summary>
public sealed class PriceDipRule : IAnalysisRule
{
    public const double MaxRatio = 0.85;
    public const double MinStock = 100;
    public const int Top = 3;

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var dips = snapshot.Economy.Goods
            .Where(g => g.PriceRatio <= MaxRatio && g.StockTotal >= MinStock && g.ExportedLast12Months > 0)
            .OrderByDescending(g => g.StockTotal * (g.AveragePrice!.Value - g.CurrentPrice!.Value))
            .Take(Top);

        foreach (var good in dips)
        {
            yield return new Finding($"trade.price-low.{good.Resource}", Severity.Info, Confidence.Uncertain,
                T("finding.priceLow", Term.Resource(good.Resource), (1 - good.PriceRatio!.Value) * 100, good.StockTotal))
            {
                Category = "Trade",
                SuggestionText = T("suggest.priceLow"),
                EvidenceTexts = [T("evidence.priceNow", good.CurrentPrice!.Value), T("evidence.priceBefore", good.AveragePrice!.Value), T("evidence.stock", good.StockTotal)],
            };
        }
    }
}

/// <summary>
/// A resource imported during the last 12 months while the island already holds at least half of that quantity in stock.
/// UNCERTAIN: imports may fill orders elsewhere (other stocks, construction), and units are not verified.
/// </summary>
public sealed class ImportWhileStockedRule : IAnalysisRule
{
    public const int MinImported = 100;
    public const double StockShare = 0.5;
    public const int Top = 3;

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var goods = snapshot.Economy.Goods
            .Where(g => g.ImportedLast12Months >= MinImported && g.StockTotal >= g.ImportedLast12Months * StockShare)
            .OrderByDescending(g => g.StockValue)
            .Take(Top);

        foreach (var good in goods)
        {
            yield return new Finding($"trade.import-while-stocked.{good.Resource}", Severity.Info, Confidence.Uncertain,
                T("finding.importWhileStocked", Term.Resource(good.Resource), (double)good.ImportedLast12Months, good.StockTotal))
            {
                Category = "Trade",
                SuggestionText = T("suggest.importWhileStocked"),
                EvidenceTexts = [T("evidence.imported", (double)good.ImportedLast12Months), T("evidence.stock", good.StockTotal)],
            };
        }
    }
}

/// <summary>Share of uneducated citizens. The education order (uneducated, high school, college) was verified against agent data.</summary>
public sealed class EducationLevelRule : IAnalysisRule
{
    public const double MinCitizens = 100;
    public const double InfoShare = 0.60;
    public const double WarningShare = 0.80;

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var education = snapshot.PopulationInfo.Education;
        var total = education.Sum();
        if (education.Count == 0 || total < MinCitizens) yield break;

        var share = education[0] / total;
        if (share < InfoShare) yield break;

        yield return new Finding("population.education-low", share >= WarningShare ? Severity.Warning : Severity.Info, Confidence.Probable,
            T("finding.educationLow", share * 100, education[0], total))
        {
            Category = "Population",
            SuggestionText = T("suggest.educationLow"),
            EvidenceTexts = education.Take(PopulationDetails.EducationLevels.Length)
                .Select((count, i) => T("evidence.educationLevel", new Term("education", PopulationDetails.EducationLevels[i]), count)).ToList(),
        };
    }
}

/// <summary>
/// Many unemployed citizens are educated. UNCERTAIN: the unemployed series is assumed to use the same education order.
/// </summary>
public sealed class EducatedJoblessRule : IAnalysisRule
{
    public const double MinEducated = 20;
    public const double MinShare = 0.40;

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var unemployed = snapshot.PopulationInfo.Unemployed;
        var total = unemployed.Sum();
        if (unemployed.Count < 2 || total <= 0) yield break;

        var educated = unemployed.Skip(1).Sum();
        if (educated < MinEducated || educated / total < MinShare) yield break;

        yield return new Finding("population.educated-jobless", Severity.Info, Confidence.Uncertain,
            T("finding.educatedJobless", educated, total, educated / total))
        {
            Category = "Population",
            SuggestionText = T("suggest.educatedJobless"),
            EvidenceTexts = unemployed.Take(PopulationDetails.EducationLevels.Length)
                .Select((count, i) => T("evidence.unemployed", new Term("education", PopulationDetails.EducationLevels[i]), count)).ToList(),
        };
    }
}
