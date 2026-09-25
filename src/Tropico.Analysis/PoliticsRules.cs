using System.Globalization;

namespace Tropico.Analysis;

/// <summary>
/// Factions whose standing modifiers add up below zero, with what causes it. The drivers (edicts, buildings, demands, history) come
/// straight from the modifiers' sources; the absolute scale of standing is unverified, hence the wording "points".
/// </summary>
public sealed class FactionStandingRule : IAnalysisRule
{
    public const double WarningBelow = -10;   // heuristic threshold on the sum of known modifiers
    public const int DriversShown = 4;

    // Hedged general leads on what each faction tends to care about.
    private static readonly Dictionary<string, string> Concerns = new()
    {
        ["Religious"] = "religion (chapels, churches)",
        ["Environmentalists"] = "pollution and nature (mines and factories weigh on them)",
        ["Militarists"] = "the army and security",
        ["Intellectuals"] = "education and culture",
        ["Industrialists"] = "industry and heavy production",
        ["Capitalists"] = "business and wealth",
        ["Communists"] = "workers and equality",
    };

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        foreach (var faction in snapshot.Politics.Factions.Where(f => f.KnownTotal < 0))
        {
            var worst = faction.Drivers.FirstOrDefault(d => d.Value < 0);
            yield return new Finding($"politics.faction-negative.{faction.Name}",
                faction.KnownTotal <= WarningBelow ? Severity.Warning : Severity.Info, Confidence.Probable,
                string.Create(CultureInfo.InvariantCulture, $"{faction.Name} sit at {faction.KnownTotal:0.0} standing points (known modifiers, leader and corruption effects not stored)."))
            {
                Category = "Politics",
                Suggestion = Suggestion(faction, worst),
                Evidence = faction.Drivers.Take(DriversShown).Select(Describe).ToList(),
            };
        }
    }

    private static string? Suggestion(FactionStanding faction, FactionDriver? worst)
    {
        if (worst is null) return null;

        return worst.Kind switch
        {
            DriverKind.Edict => $"The main cause is the edict {GameNames.Pretty(worst.Source)}: revoking or replacing it would lift this faction, at the expense of the factions it pleases.",
            DriverKind.Event => $"The main cause is {worst.Count} recent event(s) from {GameNames.Pretty(worst.Source)} buildings; each new one adds another penalty that fades over time.",
            DriverKind.Demand => $"The main cause is the outcome of a demand ({GameNames.Pretty(worst.Source)}); it fades over time.",
            DriverKind.History => HistorySuggestion(faction),
            _ => null,
        };
    }

    private static string HistorySuggestion(FactionStanding faction)
    {
        var history = faction.HistoryNegative is { } negative && faction.HistoryPositive is { } positive
            ? string.Create(CultureInfo.InvariantCulture, $"Their long-term history is negative ({negative:0} negative against {positive:0} positive events). ")
            : "";
        return Concerns.TryGetValue(faction.Name, out var concern)
            ? $"{history}This faction usually cares about {concern}: check its demands in the game."
            : $"{history}Check this faction's demands in the game.";
    }

    private static string Describe(FactionDriver driver)
    {
        var label = driver.Kind switch
        {
            DriverKind.History => "history of past actions",
            DriverKind.Event => $"{GameNames.Pretty(driver.Source)} events x{driver.Count}",
            DriverKind.Edict => $"edict {GameNames.Pretty(driver.Source)}",
            DriverKind.Demand => $"demand {GameNames.Pretty(driver.Source)}",
            _ => GameNames.Pretty(driver.Source),
        };
        return string.Create(CultureInfo.InvariantCulture, $"{label}: {driver.Value:+0.0;-0.0}");
    }
}

/// <summary>Active edicts that strongly displease some faction while pleasing others: a trade-off worth knowing about.</summary>
public sealed class EdictTradeOffRule : IAnalysisRule
{
    public const double SignificantValue = 5;

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        var effects = snapshot.Politics.Factions
            .SelectMany(f => f.Drivers.Where(d => d.Kind == DriverKind.Edict).Select(d => (Faction: f.Name, Driver: d)))
            .GroupBy(x => x.Driver.Source);

        foreach (var edict in effects.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var displeased = edict.Where(x => x.Driver.Value <= -SignificantValue).OrderBy(x => x.Driver.Value).ToList();
            if (displeased.Count == 0) continue;

            var pleased = edict.Where(x => x.Driver.Value >= SignificantValue).OrderByDescending(x => x.Driver.Value).ToList();
            var months = snapshot.Politics.Edicts.FirstOrDefault(e => e.Name == edict.Key)?.MonthsActive;

            yield return new Finding($"politics.edict.{edict.Key}", Severity.Info, Confidence.Probable,
                $"The edict {GameNames.Pretty(edict.Key)} displeases {string.Join(", ", displeased.Select(x => x.Faction))}"
                + (pleased.Count > 0 ? $" and pleases {string.Join(", ", pleased.Select(x => x.Faction))}." : "."))
            {
                Category = "Politics",
                Suggestion = "It is a trade-off: keep it while the displeased factions stay quiet, revoke it if one of them becomes a risk.",
                Evidence = edict.OrderBy(x => x.Driver.Value)
                    .Select(x => string.Create(CultureInfo.InvariantCulture, $"{x.Faction}: {x.Driver.Value:+0.0;-0.0}"))
                    .Concat(months is { } m ? [$"Active for {m:N0} months"] : []).ToList(),
            };
        }
    }
}

/// <summary>Upcoming elections, with the factions currently below zero.</summary>
public sealed class ElectionsRule : IAnalysisRule
{
    public const int WithinMonths = 24;

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        if (snapshot.Politics.MonthsUntilElections is not { } months || months is < 0 or > WithinMonths) yield break;

        var negative = snapshot.Politics.Factions.Where(f => f.KnownTotal < 0).Select(f => f.Name).ToList();
        yield return new Finding("politics.elections", negative.Count > 0 ? Severity.Warning : Severity.Info, Confidence.Probable,
            $"Elections in {months} months" + (negative.Count > 0 ? $"; factions below zero: {string.Join(", ", negative)}." : "."))
        {
            Category = "Politics",
            Suggestion = negative.Count > 0 ? "Improve the standing of the displeased factions before the vote." : null,
        };
    }
}
