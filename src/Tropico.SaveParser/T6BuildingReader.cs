using System.Text.RegularExpressions;

namespace Tropico.SaveParser;

public sealed record T6Stock(string? Resource, string? StockType, double? Size, double? Capacity);

/// <summary>
/// A placed building instance. Nullable strings mean "default value, not serialized" (the save only stores non-defaults).
/// Workers, residents and capacities are not stored as properties and are therefore not exposed.
/// </summary>
public sealed record T6Building(
    int ObjectIndex,
    string ClassName,
    string BlueprintPath,
    T6Transform? Transform,
    string? Workmode,
    string? BudgetLevel,
    string? BuildingState,
    double? ConstructionProgress,
    IReadOnlyList<string> Components,
    IReadOnlyList<T6Stock> Stocks)
{
    public bool HasResidence => Components.Contains("T6ResidenceComponent");
    public bool HasWorkplace => Components.Any(c => c.Contains("Workplace", StringComparison.Ordinal));
}

public static partial class T6BuildingReader
{
    private const string ConstructionSite = "T6ConstructionSiteComponent";

    [GeneratedRegex("Visualization|Freighter")]
    private static partial Regex Excluded();

    /// <summary>
    /// Instance = object-table record under /Game/Blueprints/Buildings/ that is a placed actor (flag 2, not a kind-3 class reference)
    /// and owns at least one component. Visualization helpers and freighters (ships) are excluded.
    /// Counting path occurrences in the data would over-count these, so it is deliberately not used.
    /// </summary>
    public static IReadOnlyList<T6Building> Read(T6SaveFile save)
    {
        var objects = save.ObjectTable.Objects;
        var components = objects
            .Where(o => o is { Flag: 1, OwnerIndex: not null })
            .ToLookup(o => o.OwnerIndex!.Value);

        var buildings = new List<T6Building>();
        foreach (var record in objects)
        {
            if (record.Kind == 3 || record.Flag != 2 || !record.Path.Contains("/Buildings/", StringComparison.Ordinal)) continue;
            if (Excluded().IsMatch(record.Path) || !components[record.Index].Any()) continue;

            var data = save.ReadObject(record);
            var owned = components[record.Index].ToList();
            var site = owned.FirstOrDefault(c => c.ShortName == ConstructionSite);
            var siteData = site is null ? null : save.ReadObject(site);

            buildings.Add(new T6Building(
                record.Index,
                record.ShortName,
                record.Path,
                data.Transform,
                Workmode: data.Find("CurrentWorkmode")?.Value.AsObjectIndex() is { } wm && wm >= 0 && wm < objects.Count ? objects[wm].ShortName : null,
                BudgetLevel: data.Find("BudgetLevel")?.Value.AsName(),
                BuildingState: siteData?.Find("BuildingState")?.Value.AsName(),
                ConstructionProgress: siteData?.Find("progress")?.Value.AsDouble(),
                Components: owned.Select(c => c.ShortName).Distinct().Order().ToList(),
                Stocks: ReadStocks(save, owned, components)));
        }

        return buildings;
    }

    // Stocks hang under the building's components (Stock.OwnerIndex = component index).
    private static List<T6Stock> ReadStocks(T6SaveFile save, List<T6ObjectRecord> owned, ILookup<int, T6ObjectRecord> components)
    {
        var stocks = new List<T6Stock>();
        foreach (var stock in owned.SelectMany(c => components[c.Index]).Where(s => s.ShortName == "T6Stock"))
        {
            var d = save.ReadObject(stock);
            stocks.Add(new T6Stock(
                d.Find("ResourceType")?.Value.AsName(), d.Find("StockType")?.Value.AsName(),
                d.Find("CurrentSize")?.Value.AsDouble(), d.Find("Capacity")?.Value.AsDouble()));
        }

        return stocks;
    }
}
