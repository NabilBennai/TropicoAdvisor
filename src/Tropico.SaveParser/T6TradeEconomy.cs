namespace Tropico.SaveParser;

/// <summary>Game date from the <c>T6Calendar</c> object. History X values are month indices: <see cref="MonthIndex"/> is the current one.</summary>
public sealed record T6Calendar(int Year, int Month, int DayInMonth, int TotalDays)
{
    /// <summary>Current game month index (TotalDays / 30). Verified: equals the X of the last history sample.</summary>
    public int MonthIndex => TotalDays / 30;
}

/// <summary>Price and trade history of one resource (from its <c>BP_T6GoodType</c> object).</summary>
public sealed record T6Good(
    string Resource,
    IReadOnlyList<string> Categories,
    IReadOnlyList<double> PriceHistory,
    IReadOnlyList<int> ImportVolumes,
    IReadOnlyList<int> ExportVolumes)
{
    public double? CurrentPrice => PriceHistory.Count > 0 ? PriceHistory[^1] : null;

    /// <summary>Volumes are the last 12 months; the last element is assumed to be the most recent month (order unverified).</summary>
    public int ExportedLast12Months => ExportVolumes.Sum();

    public int ImportedLast12Months => ImportVolumes.Sum();
}

/// <summary>A trade route a partner offers (<c>T6TradeRoute</c>). A missing bIsImport means export (the default, not serialized).</summary>
public sealed record T6TradeRouteOffer(
    int ObjectIndex, string Resource, bool IsImport, string Partner, int? VolumeGoal, string? RequiredStandingLevel);

/// <summary>A past or running contract (<c>T6ActiveTradeRoute</c>). In the reference saves every contract has ended.</summary>
public sealed record T6TradeContract(
    string Resource, bool IsImport, string Partner, int? Volume, int? StartDay, int? EndDay, string? EndReason);

/// <summary>Sum over every <c>T6Stock</c> of a resource (production, storage and transit stocks together).</summary>
public sealed record T6ResourceStock(string Resource, double Total, double Capacity, int StockCount);

/// <summary>
/// Last-year money flows attributed to a building CLASS (the game aggregates by class, not by instance).
/// Only direct income is attributed (visitor fees and rents): output sold through exports is NOT attributed to producers,
/// so a negative <see cref="Net"/> for a production building does not mean it loses money.
/// </summary>
public sealed record T6BuildingClassEconomy(string ClassName, long FeesAndRents, long Wages, long Upkeep)
{
    public long Net => FeesAndRents - Wages - Upkeep;
    public long Cost => Wages + Upkeep;
}

public sealed record T6TradeEconomy(
    T6Calendar? Calendar,
    IReadOnlyList<T6Good> Goods,
    IReadOnlyList<T6TradeRouteOffer> RouteOffers,
    IReadOnlyList<T6TradeContract> Contracts,
    IReadOnlyList<T6ResourceStock> Stocks,
    IReadOnlyList<T6BuildingClassEconomy> ClassEconomies);

public static class T6TradeEconomyReader
{
    private const string EnumSeparator = "::";
    private const string DefaultObjectPrefix = "Default__";

    public static T6TradeEconomy Read(T6SaveFile save)
    {
        var objects = save.ObjectTable.Objects;

        string ClassOf(T6Value? reference) =>
            reference.AsObjectIndex() is { } i && i >= 0 && i < objects.Count ? StripDefault(objects[i].ShortName) : "?";

        string PartnerOf(T6Value? reference) =>
            reference.AsObjectIndex() is { } i && i >= 0 && i < objects.Count ? PartnerName(objects[i].ShortName) : "?";

        var goods = new List<T6Good>();
        var offers = new List<T6TradeRouteOffer>();
        var routeByIndex = new Dictionary<int, T6TradeRouteOffer>();
        var contractsData = new List<T6ObjectData>();
        var stocks = new Dictionary<string, (double Total, double Capacity, int Count)>();
        T6Calendar? calendar = null;

        foreach (var record in objects)
        {
            if (record.Kind == 3) continue;

            switch (record.ShortName)
            {
                case "BP_T6GoodType_C":
                    goods.Add(ReadGood(save.ReadObject(record)));
                    break;
                case "T6TradeRoute":
                    var d = save.ReadObject(record);
                    var offer = new T6TradeRouteOffer(
                        record.Index, Enum(d.Find("resource")?.Value) ?? "?", (d.Find("bIsImport")?.Value as T6BoolValue)?.Value ?? false,
                        PartnerOf(d.Find("TradePartner")?.Value), (int?)d.Find("VolumeGoal")?.Value.AsInteger(),
                        Enum(d.Find("RequiredStandingLevel")?.Value));
                    offers.Add(offer);
                    routeByIndex[record.Index] = offer;
                    break;
                case "T6ActiveTradeRoute":
                    contractsData.Add(save.ReadObject(record));
                    break;
                case "T6Stock":
                    var s = save.ReadObject(record);
                    if (Enum(s.Find("ResourceType")?.Value) is not { } resource) break; // default resource: not serialized, unknown
                    var (total, capacity, count) = stocks.GetValueOrDefault(resource);
                    stocks[resource] = (total + (s.Find("CurrentSize")?.Value.AsDouble() ?? 0), capacity + (s.Find("Capacity")?.Value.AsDouble() ?? 0), count + 1);
                    break;
                case "T6Calendar":
                    var c = save.ReadObject(record);
                    calendar = new T6Calendar(
                        (int)(c.Find("Year")?.Value.AsInteger() ?? 0), (int)(c.Find("Month")?.Value.AsInteger() ?? 0),
                        (int)(c.Find("DayInMonth")?.Value.AsInteger() ?? 0), (int)(c.Find("TotalNumDays")?.Value.AsInteger() ?? 0));
                    break;
            }
        }

        var contracts = contractsData.Select(d =>
        {
            var route = d.Find("tradeRoute")?.Value.AsObjectIndex() is { } r && routeByIndex.TryGetValue(r, out var found) ? found : null;
            return new T6TradeContract(
                route?.Resource ?? "?", route?.IsImport ?? false, route?.Partner ?? "?", (int?)d.Find("CurrentVolume")?.Value.AsInteger(),
                (int?)d.Find("StartDay")?.Value.AsInteger(), (int?)d.Find("EndDay")?.Value.AsInteger(), Enum(d.Find("EndReason")?.Value));
        }).ToList();

        return new T6TradeEconomy(
            calendar, goods, offers, contracts,
            stocks.Select(kv => new T6ResourceStock(kv.Key, kv.Value.Total, kv.Value.Capacity, kv.Value.Count)).OrderByDescending(s => s.Total).ToList(),
            ReadClassEconomies(save, ClassOf));
    }

    private static T6Good ReadGood(T6ObjectData d) => new(
        Enum(d.Find("resource")?.Value) ?? "?",
        d.Find("GoodCategories")?.Value.AsItems().Select(i => Enum(i) ?? "?").ToList() ?? [],
        d.Find("PriceHistory")?.Value.AsItems().Select(i => i.AsDouble() ?? 0).ToList() ?? [],
        d.Find("ImportTradeVolumesHistory")?.Value.AsItems().Select(i => (int)(i.AsInteger() ?? 0)).ToList() ?? [],
        d.Find("ExportTradeVolumesHistory")?.Value.AsItems().Select(i => (int)(i.AsInteger() ?? 0)).ToList() ?? []);

    private static List<T6BuildingClassEconomy> ReadClassEconomies(T6SaveFile save, Func<T6Value?, string> classOf)
    {
        var collector = save.ObjectTable.Objects.FirstOrDefault(o => o.Path.EndsWith("T6DataCollector", StringComparison.Ordinal));
        if (collector is null) return [];

        var data = save.ReadObject(collector);
        var income = new Dictionary<string, long>();
        var wages = new Dictionary<string, long>();
        var upkeep = new Dictionary<string, long>();

        void Add(Dictionary<string, long> target, T6StructValue month, string key, string amount)
        {
            foreach (var entry in month.Find(key)?.Value.AsItems().OfType<T6StructValue>() ?? [])
            {
                var cls = classOf(entry.Find("building")?.Value);
                target[cls] = target.GetValueOrDefault(cls) + (entry.Find(amount)?.Value.AsInteger() ?? 0);
            }
        }

        foreach (var month in data.Find("RevenueLastYear")?.Value.AsItems().OfType<T6StructValue>() ?? [])
        {
            Add(income, month, "Fees", "Revenue");
            Add(income, month, "Rents", "Revenue");
        }

        foreach (var month in data.Find("ExpenseLastYear")?.Value.AsItems().OfType<T6StructValue>() ?? [])
        {
            Add(wages, month, "Wages", "Expenses");
            Add(upkeep, month, "Upkeeps", "Expenses");
        }

        return income.Keys.Union(wages.Keys).Union(upkeep.Keys)
            .Select(k => new T6BuildingClassEconomy(k, income.GetValueOrDefault(k), wages.GetValueOrDefault(k), upkeep.GetValueOrDefault(k)))
            .OrderBy(e => e.Net).ThenBy(e => e.ClassName, StringComparer.Ordinal)
            .ToList();
    }

    // "ET6ResourceType::Gold" -> "Gold"
    private static string? Enum(T6Value? value)
    {
        if (value.AsName() is not { } name) return null;

        var separator = name.LastIndexOf(EnumSeparator, StringComparison.Ordinal);
        return separator < 0 ? name : name[(separator + EnumSeparator.Length)..];
    }

    private static string StripDefault(string name) => name.StartsWith(DefaultObjectPrefix, StringComparison.Ordinal) ? name[DefaultObjectPrefix.Length..] : name;

    // "Default__BP_T6TradePartnerTheCrown_C" -> "TheCrown"; "Default__BP_T6TradepartnerMexico_C" -> "Mexico"
    private static string PartnerName(string shortName)
    {
        var name = StripDefault(shortName);
        if (name.EndsWith("_C", StringComparison.Ordinal)) name = name[..^2];
        foreach (var prefix in new[] { "BP_T6TradePartner", "BP_T6Tradepartner", "BP_" })
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal)) return name[prefix.Length..];
        }

        return name;
    }
}
