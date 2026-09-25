using System.Text;
using Tropico.SaveParser;

namespace Tropico.Analysis;

public enum DriverKind { History, Event, Edict, Demand, Other }

/// <summary>One cause of a faction's standing: its long-term history, a decaying event (often a building), an edict or a demand outcome.</summary>
public sealed record FactionDriver(DriverKind Kind, string Source, double Value, int Count = 1);

/// <summary>
/// Standing of a faction as the sum of the modifier values stored in the save. Leader and corruption modifiers have no stored value and
/// are excluded; the absolute scale of standing is not verified, so compare factions with each other and look at the sign.
/// </summary>
public sealed record FactionStanding(
    string Name,
    double KnownTotal,
    double? HistoryPositive,
    double? HistoryNegative,
    double? HistoryNet,
    IReadOnlyList<FactionDriver> Drivers);

public sealed record EdictInfo(string Name, int? MonthsActive, bool IsCustom);

public sealed record ConstitutionInfo(string Topic, string? Option);

public sealed record DemandInfo(string Name, string? Status);

public sealed record PoliticsSnapshot(
    string? Era,
    string? ConstitutionSigned,
    int? MonthsUntilElections,
    double? ResearchPoints,
    int? VictoryPoints,
    IReadOnlyList<string> Landmarks,
    IReadOnlyList<FactionStanding> Factions,
    IReadOnlyList<EdictInfo> Edicts,
    IReadOnlyList<ConstitutionInfo> Constitution,
    IReadOnlyList<DemandInfo> Demands)
{
    public static PoliticsSnapshot Empty { get; } = new(null, null, null, null, null, [], [], [], [], []);
}

public static class PoliticsSnapshotBuilder
{
    public static PoliticsSnapshot Build(T6Politics politics) => new(
        politics.Player?.Era,
        politics.Player is { ConstitutionSigned: true, ConstitutionMonth: { } month, ConstitutionYear: { } year } ? Date(year, month) : null,
        politics.Elections?.MonthsUntilNext,
        politics.Player?.ResearchPoints,
        politics.Player?.VictoryPoints,
        politics.Player?.Landmarks ?? [],
        politics.Factions.Select(Standing).OrderBy(f => f.KnownTotal).ThenBy(f => f.Name, StringComparer.Ordinal).ToList(),
        politics.Edicts.Where(e => e.IsActive).Select(e => new EdictInfo(e.Name, e.MonthsActive, e.IsCustom)).ToList(),
        politics.Constitution.Select(c => new ConstitutionInfo(c.Topic, c.ActiveOption)).ToList(),
        politics.Demands.Select(d => new DemandInfo(d.Name, d.Status)).ToList());

    private static FactionStanding Standing(T6Faction faction)
    {
        var drivers = faction.Modifiers
            .Where(m => m.Value is not null and not 0)
            .GroupBy(m => (Kind: Classify(m), Source: m.IsHistory ? faction.Name : m.SourceClass))
            .Select(g => new FactionDriver(g.Key.Kind, g.Key.Source, g.Sum(m => m.Value!.Value), g.Count()))
            .OrderBy(d => d.Value).ThenBy(d => d.Source, StringComparer.Ordinal)
            .ToList();

        return new FactionStanding(faction.Name, faction.KnownTotal, faction.HistoryPositive, faction.HistoryNegative, faction.HistoryNet, drivers);
    }

    private static DriverKind Classify(T6StandingModifier modifier) =>
        modifier.IsHistory ? DriverKind.History
        : modifier.IsFleeting ? DriverKind.Event
        : modifier.IsEdictEffect ? DriverKind.Edict
        : modifier.Kind.Contains("RewardStanding", StringComparison.Ordinal) || modifier.Kind.Contains("PenaltyStanding", StringComparison.Ordinal) ? DriverKind.Demand
        : DriverKind.Other;

    private static readonly string[] MonthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static string Date(int year, int month) => month is >= 1 and <= 12 ? $"{MonthNames[month - 1]} {year}" : $"{year}";
}

/// <summary>Readable names for game identifiers used in messages: "BP_T6EdictEmployeeOfTheMonth_C" -> "Employee Of The Month".</summary>
public static class GameNames
{
    public static string Pretty(string name)
    {
        var value = name;
        if (value.EndsWith("_C", StringComparison.Ordinal)) value = value[..^2];
        foreach (var prefix in new[] { "BP_T6ConstitutionTopic", "BP_T6ConstitutionOption", "BP_T6Constitution", "BP_T6Edict", "BP_Edict_", "BP_T6_", "BP_Building_", "BP_", "Building_" })
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                value = value[prefix.Length..];
                break;
            }
        }

        if (value.Length > 2 && value.StartsWith("T6", StringComparison.Ordinal) && char.IsUpper(value[2])) value = value[2..];

        var text = new StringBuilder(value.Length + 4);
        var previous = '\0';
        foreach (var c in value.Replace('_', ' '))
        {
            if (previous != '\0' && char.IsUpper(c) && (char.IsLower(previous) || char.IsDigit(previous))) text.Append(' ');
            text.Append(c);
            previous = c;
        }

        return text.ToString().Trim();
    }
}
