namespace Tropico.SaveParser;

/// <summary>
/// One effect on a faction's standing. <see cref="Value"/> is null for kinds whose value is computed at runtime and not stored
/// (leader and corruption modifiers). <see cref="Kind"/> is the modifier class, e.g. <c>T6StandingModifierFleeting</c>.
/// </summary>
public sealed record T6StandingModifier(string Kind, double? Value, double? StartValue, string SourceClass, string? TargetClass)
{
    public bool IsHistory => Kind == "T6StandingModifierDynamicHistory";
    public bool IsFleeting => Kind == "T6StandingModifierFleeting";
    public bool IsConstant => Kind == "T6StandingModifierConstant";

    /// <summary>Constant modifiers whose source is an edict, e.g. "Food for the People" (-10 for Capitalists).</summary>
    public bool IsEdictEffect => IsConstant && SourceClass.Contains("Edict", StringComparison.Ordinal);
}

/// <summary>Faction with its active standing modifiers. The absolute scale of standing is unverified; totals are sums of the stored values.</summary>
public sealed record T6Faction(
    int ObjectIndex,
    string Name,
    IReadOnlyList<T6StandingModifier> Modifiers,
    double? HistoryPositive,
    double? HistoryNegative,
    double? HistoryNet)
{
    /// <summary>Sum of every stored modifier value; excludes leader and corruption modifiers, whose values are not in the save.</summary>
    public double KnownTotal => Modifiers.Sum(m => m.Value ?? 0);
}

public sealed record T6Edict(string Name, bool IsActive, int? MonthsActive, bool IsCustom);

public sealed record T6ConstitutionChoice(string Topic, string? ActiveOption);

/// <summary>A faction or superpower demand (quest). Status is e.g. Rewarded or Abandoned.</summary>
public sealed record T6Demand(string Name, string? Status, int? OfferedDay);

public sealed record T6PlayerPolitics(
    string? Era,
    bool ConstitutionSigned,
    int? ConstitutionMonth,
    int? ConstitutionYear,
    double? ResearchPoints,
    int? VictoryPoints,
    IReadOnlyList<string> Landmarks);

public sealed record T6Elections(int? MonthsUntilNext, bool? Enabled, int? LastElectionDay);

public sealed record T6Politics(
    T6PlayerPolitics? Player,
    T6Elections? Elections,
    IReadOnlyList<T6Faction> Factions,
    IReadOnlyList<T6Edict> Edicts,
    IReadOnlyList<T6ConstitutionChoice> Constitution,
    IReadOnlyList<T6Demand> Demands);

public static class T6PoliticsReader
{
    private const string FactionPrefix = "BP_Faction_";
    private const string EnumSeparator = "::";

    public static T6Politics Read(T6SaveFile save)
    {
        var objects = save.ObjectTable.Objects;
        T6ObjectRecord? Ref(T6Value? value) => value.AsObjectIndex() is { } i && i >= 0 && i < objects.Count ? objects[i] : null;
        string ClassOf(T6Value? value) => Ref(value) is { } record ? StripDefault(record.ShortName) : "?";

        T6Faction? ReadFaction(T6ObjectRecord record)
        {
            var data = save.ReadObject(record);
            var modifiers = new List<T6StandingModifier>();
            double? positive = null, negative = null, net = null;

            foreach (var modifierRef in data.Find("ActiveModifiers")?.Value.AsItems() ?? [])
            {
                if (Ref(modifierRef) is not { } modifierRecord) continue;

                var m = save.ReadObject(modifierRecord);
                var value = m.Find("CurrentValue")?.Value.AsDouble() ?? m.Find("Value")?.Value.AsDouble();
                var modifier = new T6StandingModifier(
                    modifierRecord.ShortName, value, m.Find("StartValue")?.Value.AsDouble(),
                    ClassOf(m.Find("Source")?.Value), m.Find("Target") is { } target ? ClassOf(target.Value) : null);
                modifiers.Add(modifier);

                if (modifier.IsHistory)
                {
                    positive = m.Find("ServerHistoryAccountPositive")?.Value.AsDouble();
                    negative = m.Find("ServerHistoryAccountNegative")?.Value.AsDouble();
                    net = m.Find("ServerHistoryAccountNet")?.Value.AsDouble();
                }
            }

            return new T6Faction(record.Index, record.ShortName[FactionPrefix.Length..].Replace("_C", ""), modifiers, positive, negative, net);
        }

        var factions = objects
            .Where(o => o.Kind != 3 && o.ShortName.StartsWith(FactionPrefix, StringComparison.Ordinal) && o.HasBlob)
            .Select(ReadFaction).OfType<T6Faction>().ToList();

        return new T6Politics(
            ReadPlayer(save, objects),
            ReadElections(save, objects),
            factions,
            ReadEdicts(save, objects),
            ReadConstitution(save, objects),
            ReadDemands(save, objects));
    }

    private static T6PlayerPolitics? ReadPlayer(T6SaveFile save, IReadOnlyList<T6ObjectRecord> objects)
    {
        var record = objects.FirstOrDefault(o => o.Kind != 3 && o.ShortName == "BP_T6PlayerState_C");
        if (record is null) return null;

        var d = save.ReadObject(record);
        return new T6PlayerPolitics(
            Enum(d.Find("PlayerEra")?.Value),
            (d.Find("bConstitutionSigned")?.Value as T6BoolValue)?.Value ?? false,
            (int?)d.Find("ConstitutionSignedMonth")?.Value.AsInteger(), (int?)d.Find("ConstitutionSignedYear")?.Value.AsInteger(),
            d.Find("ResearchPoints")?.Value.AsDouble(), (int?)d.Find("VictoryPoints")?.Value.AsInteger(),
            d.Find("BuiltLandmarks")?.Value.AsItems().Select(l => Enum(l) ?? "?").ToList() ?? []);
    }

    private static T6Elections? ReadElections(T6SaveFile save, IReadOnlyList<T6ObjectRecord> objects)
    {
        var record = objects.FirstOrDefault(o => o.Kind != 3 && o.ShortName == "BP_T6ElectionsManager_C");
        if (record is null) return null;

        var d = save.ReadObject(record);
        return new T6Elections(
            (int?)d.Find("MonthsUntilNextElections")?.Value.AsInteger(), (d.Find("IsElectionEnabled")?.Value as T6BoolValue)?.Value,
            (int?)d.Find("LastElectionDate")?.Value.AsInteger());
    }

    private static List<T6Edict> ReadEdicts(T6SaveFile save, IReadOnlyList<T6ObjectRecord> objects)
    {
        var manager = objects.FirstOrDefault(o => o.Kind != 3 && o.ShortName == "BP_T6EdictManager_C");
        if (manager is null) return [];

        var data = save.ReadObject(manager);
        var edicts = new List<T6Edict>();
        foreach (var (property, custom) in new[] { ("Edicts", false), ("CustomEdicts", true) })
        {
            foreach (var reference in data.Find(property)?.Value.AsItems() ?? [])
            {
                if (reference.AsObjectIndex() is not { } i || i < 0 || i >= objects.Count) continue; // -1 = empty slot

                var edict = save.ReadObject(objects[i]);
                edicts.Add(new T6Edict(
                    StripDefault(objects[i].ShortName), (edict.Find("bIsActive")?.Value as T6BoolValue)?.Value ?? false,
                    (int?)edict.Find("MonthsActive")?.Value.AsInteger(), custom));
            }
        }

        return edicts;
    }

    // Only topics with a serialized ActiveOption are listed; the others hold their default option, which is not stored.
    private static List<T6ConstitutionChoice> ReadConstitution(T6SaveFile save, IReadOnlyList<T6ObjectRecord> objects)
    {
        var choices = new List<T6ConstitutionChoice>();
        foreach (var record in objects.Where(o => o.Kind != 3 && o.ShortName.StartsWith("BP_T6Constitution", StringComparison.Ordinal) && o.HasBlob))
        {
            if (save.ReadObject(record).Find("ActiveOption")?.Value.AsObjectIndex() is not { } option || option < 0 || option >= objects.Count) continue;

            choices.Add(new T6ConstitutionChoice(record.ShortName, StripDefault(objects[option].ShortName)));
        }

        return choices;
    }

    private static List<T6Demand> ReadDemands(T6SaveFile save, IReadOnlyList<T6ObjectRecord> objects)
    {
        var demands = new List<T6Demand>();
        foreach (var record in objects.Where(o => o.Kind != 3 && o.HasBlob && (o.ShortName.Contains("FactionDemand", StringComparison.Ordinal) || o.ShortName.Contains("SuperpowerDemand", StringComparison.Ordinal))))
        {
            var d = save.ReadObject(record);
            demands.Add(new T6Demand(record.ShortName, Enum(d.Find("QuestStatus")?.Value), (int?)d.Find("TimeFirstOffered")?.Value.AsInteger()));
        }

        return demands;
    }

    private static string? Enum(T6Value? value)
    {
        if (value.AsName() is not { } name) return null;

        var separator = name.LastIndexOf(EnumSeparator, StringComparison.Ordinal);
        return separator < 0 ? name : name[(separator + EnumSeparator.Length)..];
    }

    private static string StripDefault(string name) => name.StartsWith("Default__", StringComparison.Ordinal) ? name["Default__".Length..] : name;
}
