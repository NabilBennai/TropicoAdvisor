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
    /// <summary>Monthly price samples (oldest to newest assumed, unverified).</summary>
    public IReadOnlyList<double> PriceHistory { get; init; } = [];

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
    /// <summary>Current game date (from the calendar).</summary>
    public int? Year { get; init; }

    public int? Month { get; init; }

    /// <summary>Last-year revenue and expenses by category (semantics partly unverified).</summary>
    public IReadOnlyDictionary<string, long> RevenueByCategory { get; init; } = new Dictionary<string, long>();

    public IReadOnlyDictionary<string, long> ExpensesByCategory { get; init; } = new Dictionary<string, long>();

    /// <summary>Monthly revenue and expense history; X is the game month index.</summary>
    public IReadOnlyList<TimePoint> RevenueHistory { get; init; } = [];

    public IReadOnlyList<TimePoint> ExpenseHistory { get; init; } = [];

    /// <summary>Calendar (year, month 1-12) of a history month index, derived from the current date; null without a calendar.</summary>
    public (int Year, int Month)? DateOf(double monthIndex) => GameCalendar.DateOf(MonthIndex, Year, Month, monthIndex);

    public static EconomySnapshot Empty { get; } = new(null, [], [], 0, 0, 0, 0, 0, 0, new Dictionary<string, long>());
}
