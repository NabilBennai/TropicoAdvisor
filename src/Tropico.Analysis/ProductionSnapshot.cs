using Tropico.SaveParser;

namespace Tropico.Analysis;

/// <summary>An output stock of a producing building. A missing current size in the save means 0 (default value, not serialized).</summary>
public sealed record ProducerStock(int BuildingIndex, string ClassName, string Resource, double Size, double Capacity)
{
    public const double FullThreshold = 0.9;

    public double Fill => Capacity > 0 ? Size / Capacity : 0;

    public bool IsFull => Capacity > 0 && Fill >= FullThreshold;
}

/// <summary>Buildings of one class. States other than Built are listed; workmode and budget level are per building.</summary>
public sealed record BuildingClassSummary(
    string ClassName,
    int Count,
    IReadOnlyDictionary<string, int> States,
    IReadOnlyDictionary<string, int> Workmodes,
    IReadOnlyDictionary<string, int> BudgetLevels,
    IReadOnlyList<string> Outputs,
    double? AverageOutputFill,
    int FullOutputs);

/// <summary>Production side of one resource: output stocks of producers, input stocks of consumers, and the island-wide stock.</summary>
public sealed record ResourceProduction(
    string Resource,
    int Producers,
    double OutputStock,
    double OutputCapacity,
    int FullProducers,
    int ConsumerStocks,
    double IslandStock,
    int ExportedLast12Months)
{
    public double Fill => OutputCapacity > 0 ? OutputStock / OutputCapacity : 0;
}

/// <summary>
/// Deposits of a resource. <see cref="Tapped"/> is a HEURISTIC: each producer of the resource is matched to the nearest deposit of
/// that resource (within <see cref="ProductionSnapshotBuilder.MaxTapDistance"/> map units), because the save has no explicit link.
/// </summary>
public sealed record DepositSummary(string Resource, int Deposits, int Tapped, double? AmountEach, int Producers);

public sealed record ProductionSnapshot(
    IReadOnlyList<BuildingClassSummary> Classes,
    IReadOnlyList<ResourceProduction> Resources,
    IReadOnlyList<DepositSummary> Deposits,
    IReadOnlyList<ProducerStock> FullOutputs,
    int Producers)
{
    public static ProductionSnapshot Empty { get; } = new([], [], [], [], 0);
}

public static class ProductionSnapshotBuilder
{
    public const double MaxTapDistance = 8_000; // unverified unit (map units); observed mine-to-deposit distances are 1,700-5,200

    private const string OutStock = "OutStock";
    private const string InStock = "InStock";

    public static ProductionSnapshot Build(IReadOnlyList<T6Building> buildings, IReadOnlyList<T6Deposit> deposits, T6TradeEconomy trade)
    {
        var outputs = buildings
            .SelectMany(b => b.Stocks.Where(s => IsKind(s, OutStock) && s.Resource is not null)
                .Select(s => new ProducerStock(b.ObjectIndex, b.ClassName, Name(s.Resource!), s.Size ?? 0, s.Capacity ?? 0)))
            .ToList();
        var inputs = buildings.SelectMany(b => b.Stocks.Where(s => IsKind(s, InStock) && s.Resource is not null)).ToList();
        var islandStock = trade.Stocks.ToDictionary(s => Name(s.Resource), s => s.Total);
        var exported = trade.Goods.ToDictionary(g => Name(g.Resource), g => g.ExportedLast12Months);

        var classes = buildings.GroupBy(b => b.ClassName).Select(group =>
        {
            var own = outputs.Where(o => o.ClassName == group.Key).ToList();
            return new BuildingClassSummary(
                group.Key, group.Count(),
                Count(group.Select(b => StateName(b.BuildingState)).Where(s => s != "Built" && s != IslandSnapshotBuilder.UnknownState)),
                Count(group.Select(b => b.Workmode ?? "-")),
                Count(group.Select(b => LevelName(b.BudgetLevel))),
                own.Select(o => o.Resource).Distinct().Order().ToList(),
                own.Count > 0 ? own.Average(o => o.Fill) : null,
                own.Count(o => o.IsFull));
        }).OrderByDescending(c => c.Count).ThenBy(c => c.ClassName, StringComparer.Ordinal).ToList();

        var resources = outputs.GroupBy(o => o.Resource).Select(group => new ResourceProduction(
                group.Key,
                group.Select(o => o.BuildingIndex).Distinct().Count(),
                group.Sum(o => o.Size), group.Sum(o => o.Capacity), group.Count(o => o.IsFull),
                inputs.Count(i => Name(i.Resource!) == group.Key),
                islandStock.GetValueOrDefault(group.Key), exported.GetValueOrDefault(group.Key)))
            .OrderByDescending(r => r.OutputStock).ThenBy(r => r.Resource, StringComparer.Ordinal).ToList();

        return new ProductionSnapshot(
            classes, resources, Deposits(buildings, deposits, outputs),
            outputs.Where(o => o.IsFull).OrderByDescending(o => o.Fill).ThenBy(o => o.BuildingIndex).ToList(),
            outputs.Select(o => o.BuildingIndex).Distinct().Count());
    }

    private static List<DepositSummary> Deposits(IReadOnlyList<T6Building> buildings, IReadOnlyList<T6Deposit> deposits, List<ProducerStock> outputs)
    {
        var position = buildings.Where(b => b.Transform is not null).ToDictionary(b => b.ObjectIndex, b => b.Transform!);
        var result = new List<DepositSummary>();

        foreach (var group in deposits.GroupBy(d => d.Resource).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var tapped = new HashSet<int>();
            var producers = outputs.Where(o => o.Resource == group.Key).Select(o => o.BuildingIndex).Distinct().ToList();
            foreach (var producer in producers.Where(position.ContainsKey))
            {
                var nearest = group.Select(d => (Deposit: d, Distance: Math.Sqrt(Math.Pow(position[producer].X - d.X, 2) + Math.Pow(position[producer].Y - d.Y, 2))))
                    .MinBy(x => x.Distance);
                if (nearest.Distance <= MaxTapDistance) tapped.Add(nearest.Deposit.ObjectIndex);
            }

            result.Add(new DepositSummary(group.Key, group.Count(), tapped.Count, group.Average(d => d.Remaining ?? 0), producers.Count));
        }

        return result;
    }

    private static bool IsKind(T6Stock stock, string kind) => stock.StockType?.EndsWith(kind, StringComparison.Ordinal) == true;

    private static Dictionary<string, int> Count(IEnumerable<string> values) => values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());

    // "ET6ResourceType::Coal" -> "Coal"
    private static string Name(string value)
    {
        var separator = value.LastIndexOf("::", StringComparison.Ordinal);
        return separator < 0 ? value : value[(separator + 2)..];
    }

    private static string StateName(string? state) => state is null ? IslandSnapshotBuilder.UnknownState : Name(state);

    // absent BudgetLevel = default, not serialized
    private static string LevelName(string? level) => level is null ? "-" : Name(level);
}
