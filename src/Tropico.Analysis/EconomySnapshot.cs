namespace Tropico.Analysis;

/// <summary>Price, stock and trade situation of one resource. Stock is the sum over every stock of the resource on the island.</summary>
public sealed record GoodSnapshot(
    string Resource,
    double? CurrentPrice,
    double? AveragePrice,
    double StockTotal,
    int ExportedLast12Months,
    int ImportedLast12Months,
    int ExportOffers,
    int ImportOffers,
    IReadOnlyList<string> ExportPartners)
{
    /// <summary>Approximate value of the stock at the current price.</summary>
    public double StockValue => StockTotal * (CurrentPrice ?? 0);

    /// <summary>Current price relative to the historical average (1.10 = 10 % above); null without enough history.</summary>
    public double? PriceRatio => CurrentPrice is { } price && AveragePrice is > 0 ? price / AveragePrice : null;
}

/// <summary>Last-year money flows of a building class. Production classes have no direct income (exports are not attributed to them).</summary>
public sealed record ClassCost(string ClassName, int Instances, long Wages, long Upkeep, long FeesAndRents)
{
    public long Cost => Wages + Upkeep;
}

/// <summary>Economy figures used by the economic rules. Yearly figures are sums of the 12 last-year buckets (semantics partly unverified).</summary>
public sealed record EconomySnapshot(
    int? MonthIndex,
    IReadOnlyList<GoodSnapshot> Goods,
    IReadOnlyList<ClassCost> ClassCosts,
    long YearlyRevenue,
    long YearlyExpenses,
    long Wages,
    long Upkeep,
    long Imports,
    long ExportRevenue,
    IReadOnlyDictionary<string, long> ExportRevenueByResource)
{
    public static EconomySnapshot Empty { get; } = new(null, [], [], 0, 0, 0, 0, 0, 0, new Dictionary<string, long>());
}
