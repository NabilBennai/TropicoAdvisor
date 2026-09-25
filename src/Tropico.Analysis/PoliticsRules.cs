using Tropico.Localization;
using static Tropico.Analysis.L;

namespace Tropico.Analysis;

/// <summary>
/// Factions whose standing modifiers add up below zero, with what causes it. The drivers (edicts, buildings, demands, history) come
/// straight from the modifiers' sources; the absolute scale of standing is unverified, hence the wording "points".
/// </summary>
public sealed class FactionStandingRule : IAnalysisRule
{
    public const double WarningBelow = -10;   // heuristic threshold on the sum of known modifiers
    public const int DriversShown = 4;

    // Factions with a hedged general lead on what they tend to care about (catalog keys concern.*).
    private static readonly HashSet<string> WithConcern =
        ["Religious", "Environmentalists", "Militarists", "Intellectuals", "Industrialists", "Capitalists", "Communists"];

    public IEnumerable<Finding> Evaluate(IslandSnapshot snapshot)
    {
        foreach (var faction in snapshot.Politics.Factions.Where(f => f.KnownTotal < 0))
        {
            var worst = faction.Drivers.FirstOrDefault(d => d.Value < 0);
            yield return new Finding($"politics.faction-negative.{faction.Name}",
                faction.KnownTotal <= WarningBelow ? Severity.Warning : Severity.Info, Confidence.Probable,
                T("finding.factionNegative", Term.Faction(faction.Name), faction.KnownTotal))
            {
                Category = "Politics",
                SuggestionText = Suggestion(faction, worst),
                EvidenceTexts = faction.Drivers.Take(DriversShown).Select(Describe).ToList(),
            };
        }
    }

    private static LocalizedText? Suggestion(FactionStanding faction, FactionDriver? worst)
    {
        if (worst is null) return null;

        return worst.Kind switch
        {
            DriverKind.Edict => T("suggest.faction.edict", GameNames.Pretty(worst.Source)),
            DriverKind.Event => T("suggest.faction.event", worst.Count, GameNames.Pretty(worst.Source)),
            DriverKind.Demand => T("suggest.faction.demand", GameNames.Pretty(worst.Source)),
            DriverKind.History => HistorySuggestion(faction),
            _ => null,
        };
    }

    private static LocalizedText HistorySuggestion(FactionStanding faction)
    {
        var hasConcern = WithConcern.Contains(faction.Name);
        var concern = hasConcern ? T("concern." + faction.Name) : null;

        if (faction is { HistoryNegative: { } negative, HistoryPositive: { } positive })
        {
            return hasConcern
                ? T("suggest.faction.history.full", negative, positive, concern)
                : T("suggest.faction.history.noConcern", negative, positive);
        }

        return hasConcern ? T("suggest.faction.history.noStats", concern) : T("suggest.faction.history.none");
    }

    private static LocalizedText Describe(FactionDriver driver)
    {
        var label = driver.Kind switch
        {
            DriverKind.History => T("driver.history"),
            DriverKind.Event => T("driver.event", GameNames.Pretty(driver.Source), driver.Count),
            DriverKind.Edict => T("driver.edict", GameNames.Pretty(driver.Source)),
            DriverKind.Demand => T("driver.demand", GameNames.Pretty(driver.Source)),
            _ => LocalizedText.Raw(GameNames.Pretty(driver.Source)),
        };
        return T("evidence.driver", label, driver.Value);
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

            var name = GameNames.Pretty(edict.Key);
            var displeasedList = TextList.Of(displeased.Select(x => (object?)Term.Faction(x.Faction)));
            var evidence = edict.OrderBy(x => x.Driver.Value).Select(x => T("evidence.edictFaction", Term.Faction(x.Faction), x.Driver.Value)).ToList();
            if (months is { } m) evidence.Add(T("evidence.activeMonths", m));

            yield return new Finding($"politics.edict.{edict.Key}", Severity.Info, Confidence.Probable,
                pleased.Count > 0
                    ? T("finding.edict.both", name, displeasedList, TextList.Of(pleased.Select(x => (object?)Term.Faction(x.Faction))))
                    : T("finding.edict.only", name, displeasedList))
            {
                Category = "Politics",
                SuggestionText = T("suggest.edict"),
                EvidenceTexts = evidence,
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

        var negative = snapshot.Politics.Factions.Where(f => f.KnownTotal < 0).Select(f => (object?)Term.Faction(f.Name)).ToList();
        yield return new Finding("politics.elections", negative.Count > 0 ? Severity.Warning : Severity.Info, Confidence.Probable,
            negative.Count > 0 ? T("finding.elections.withNegative", months, TextList.Of(negative)) : T("finding.elections", months))
        {
            Category = "Politics",
            SuggestionText = negative.Count > 0 ? T("suggest.elections") : null,
        };
    }
}
