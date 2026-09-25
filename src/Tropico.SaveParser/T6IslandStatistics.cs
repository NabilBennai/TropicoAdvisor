namespace Tropico.SaveParser;

public sealed record T6HistorySample(double X, IReadOnlyList<double> Y);

/// <summary>Citizen counters stored by the game's data collector. Null = not present in the save.</summary>
public sealed record T6PopulationCounters(
    int? Total, int? Children, int? Adults, int? Retired, int? Prisoners, int? Soldiers, int? Voters, int? NativeTropicans, int? Immigrants);

/// <summary>
/// Sums of the RevenueLastYear / ExpenseLastYear buckets (12 monthly entries). UNCERTAIN: the totals do not reconcile exactly
/// with <see cref="T6IslandStatistics.MonthBalance"/> (observed on the reference save), so their exact meaning is unverified.
/// </summary>
public sealed record T6YearlyFinances(
    IReadOnlyDictionary<string, long> RevenueByCategory,
    IReadOnlyDictionary<string, long> ExpensesByCategory,
    IReadOnlyDictionary<string, long> ExportRevenueByResource,
    IReadOnlyDictionary<string, long> ImportExpenseByResource)
{
    public long TotalRevenue => RevenueByCategory.Values.Sum();
    public long TotalExpenses => ExpensesByCategory.Values.Sum();
}

/// <summary>
/// Island statistics from the game's <c>T6DataCollector</c> object.
/// History X values are game-time indices (unit unverified); "last sample" can lag the live game by one sampling interval.
/// The meaning of the Y series of several histories (unemployment by education, homeless families by wealth...) is inferred, not verified.
/// </summary>
public sealed class T6IslandStatistics
{
    public required int CollectorObjectIndex { get; init; }
    public required T6PopulationCounters Population { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<T6HistorySample>> Histories { get; init; }
    public required IReadOnlyList<double> MonthBalance { get; init; }
    public required T6YearlyFinances YearlyFinances { get; init; }

    public T6HistorySample? LastSample(string history) =>
        Histories.TryGetValue(history, out var samples) ? samples.LastOrDefault() : null;

    /// <summary>First Y value of the last TreasuryHistory sample.</summary>
    public double? Treasury => LastSample("TreasuryHistory")?.Y.Cast<double?>().FirstOrDefault();

    /// <summary>First Y value of the last SwissHistory sample (0 = no account or empty).</summary>
    public double? SwissBank => LastSample("SwissHistory")?.Y.Cast<double?>().FirstOrDefault();
}

public static class T6IslandStatisticsReader
{
    private static readonly string[] RevenueCategories = ["Exports", "Fees", "Rents", "TouristFees", "TouristRents"];
    private static readonly string[] ExpenseCategories = ["Imports", "Constructions", "Upkeeps", "Wages"];

    public static T6IslandStatistics Read(T6SaveFile save)
    {
        var record = save.ObjectTable.Objects.FirstOrDefault(o => o.Path.EndsWith("T6DataCollector", StringComparison.Ordinal))
            ?? throw new InvalidDataException("T6DataCollector object not found in save.");
        var data = save.ReadObject(record);

        int? Counter(string name) => (int?)data.Find(name)?.Value.AsInteger();

        var histories = new Dictionary<string, IReadOnlyList<T6HistorySample>>();
        foreach (var property in data.Properties)
        {
            if (property.Value is T6StructValue { StructName: "T6HistoricalData" } history)
            {
                histories[property.Name] = history.Find("Entries")?.Value.AsItems().OfType<T6StructValue>().Select(ToSample).ToList() ?? [];
            }
        }

        var revenue = data.Find("RevenueLastYear")?.Value.AsItems().OfType<T6StructValue>().ToList() ?? [];
        var expenses = data.Find("ExpenseLastYear")?.Value.AsItems().OfType<T6StructValue>().ToList() ?? [];

        var revenueByCategory = RevenueCategories.ToDictionary(c => c, c => SumEntries(revenue, c, "Revenue"));
        revenueByCategory["CustomMiscs"] = SumEntries(revenue, "CustomMiscs", "Value");
        revenueByCategory["SuperpowerAid"] = SumFixedArray(revenue, "SuperpowerAid");

        var expensesByCategory = ExpenseCategories.ToDictionary(c => c, c => SumEntries(expenses, c, "Expenses"));
        expensesByCategory["CustomMiscs"] = SumEntries(expenses, "CustomMiscs", "Value");
        expensesByCategory["CelebWages"] = SumFixedArray(expenses, "CelebWages");

        return new T6IslandStatistics
        {
            CollectorObjectIndex = record.Index,
            Population = new T6PopulationCounters(
                Counter("TotalCitizensNum"), Counter("ChildrenNum"), Counter("AdultsNum"), Counter("RetiredNum"), Counter("PrisonersNum"),
                Counter("SoldiersNum"), Counter("VotersNum"), Counter("NativeTropicanNum"), Counter("ImmigrantsNum")),
            Histories = histories,
            MonthBalance = data.Find("MonthBalance")?.Value.AsItems().OfType<T6StructValue>()
                .Select(s => s.Find("Value")?.Value.AsDouble() ?? 0).ToList() ?? [],
            YearlyFinances = new T6YearlyFinances(
                revenueByCategory, expensesByCategory,
                ByResource(revenue, "Exports", "Revenue"), ByResource(expenses, "Imports", "Expenses")),
        };
    }

    private static T6HistorySample ToSample(T6StructValue entry) => new(
        entry.Find("ValueX")?.Value.AsDouble() ?? 0,
        entry.Find("ValuesY")?.Value.AsItems().Select(v => v.AsDouble() ?? 0).ToList() ?? []);

    // Sum of <sub> over every element of the array property <key>, over all monthly buckets.
    private static long SumEntries(List<T6StructValue> months, string key, string sub) =>
        months.SelectMany(m => m.Find(key)?.Value.AsItems().OfType<T6StructValue>() ?? [])
            .Sum(e => e.Find(sub)?.Value.AsInteger() ?? 0);

    // Fixed C arrays (SuperpowerAid, CelebWages) are stored as several properties with the same name and different array indices.
    private static long SumFixedArray(List<T6StructValue> months, string name) =>
        months.SelectMany(m => m.Properties).Where(p => p.Name == name).Sum(p => p.Value.AsInteger() ?? 0);

    private static Dictionary<string, long> ByResource(List<T6StructValue> months, string key, string sub)
    {
        var result = new Dictionary<string, long>();
        foreach (var entry in months.SelectMany(m => m.Find(key)?.Value.AsItems().OfType<T6StructValue>() ?? []))
        {
            var resource = entry.Find("resource")?.Value.AsName() ?? "?";
            result[resource] = result.GetValueOrDefault(resource) + (entry.Find(sub)?.Value.AsInteger() ?? 0);
        }

        return result;
    }
}
