using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tropico.Analysis;

namespace Tropico.Desktop.ViewModels;

public sealed record FactionRow(string Name, string Standing, bool IsNegative, string History, string Causes);

public sealed record EdictRow(string Name, string Active);

public sealed record ConstitutionRow(string Topic, string Option);

public sealed record DemandRow(string Name, string Status);

public sealed class PoliticsViewModel
{
    private const int CausesShown = 3;

    public PoliticsViewModel(IslandSnapshot snapshot, IReadOnlyList<Finding> findings)
    {
        var politics = snapshot.Politics;

        Era = politics.Era is null ? "?" : DisplayNames.Category(politics.Era);
        Constitution = politics.ConstitutionSigned ?? "not signed";
        Elections = politics.MonthsUntilElections is { } months ? $"in {months.ToString("N0", CultureInfo.CurrentCulture)} months" : "?";
        Research = politics.ResearchPoints is { } research ? research.ToString("N0", CultureInfo.CurrentCulture) : "?";
        VictoryPoints = politics.VictoryPoints is { } victory ? victory.ToString("N0", CultureInfo.CurrentCulture) : "?";

        Factions = politics.Factions.Select(f => new FactionRow(
            DisplayNames.Category(f.Name),
            f.KnownTotal.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture),
            f.KnownTotal < 0,
            f.HistoryNet is { } net ? $"{net:+0;-0;0} ({f.HistoryPositive:0} +, {f.HistoryNegative:0} -)" : "-",
            Causes(f))).ToList();
        Edicts = politics.Edicts.OrderBy(e => e.IsCustom).ThenByDescending(e => e.MonthsActive ?? 0)
            .Select(e => new EdictRow(GameNames.Pretty(e.Name) + (e.IsCustom ? " (custom)" : ""), e.MonthsActive is { } m ? $"{m.ToString("N0", CultureInfo.CurrentCulture)} months" : "-"))
            .ToList();
        ConstitutionChoices = politics.Constitution.Select(c => new ConstitutionRow(GameNames.Pretty(c.Topic), c.Option is null ? "-" : GameNames.Pretty(c.Option))).ToList();
        Demands = politics.Demands.OrderBy(d => d.Status, System.StringComparer.Ordinal).ThenBy(d => d.Name, System.StringComparer.Ordinal)
            .Select(d => new DemandRow(GameNames.Pretty(d.Name), d.Status ?? "-")).ToList();
        Landmarks = politics.Landmarks.Count == 0 ? "none" : string.Join(", ", politics.Landmarks.Select(DisplayNames.Category));
        Findings = findings.Where(f => f.Category == "Politics").Select(f => new FindingViewModel(f)).ToList();
    }

    public string Era { get; }
    public string Constitution { get; }
    public string Elections { get; }
    public string Research { get; }
    public string VictoryPoints { get; }
    public string Landmarks { get; }

    public IReadOnlyList<FactionRow> Factions { get; }
    public IReadOnlyList<EdictRow> Edicts { get; }
    public IReadOnlyList<ConstitutionRow> ConstitutionChoices { get; }
    public IReadOnlyList<DemandRow> Demands { get; }
    public IReadOnlyList<FindingViewModel> Findings { get; }
    public bool HasFindings => Findings.Count > 0;

    // The largest effects, negative first: "Mine events x4 -6.7; history -2.7".
    private static string Causes(FactionStanding faction)
    {
        if (faction.Drivers.Count == 0) return "-";

        return string.Join("; ", faction.Drivers.OrderByDescending(d => System.Math.Abs(d.Value)).Take(CausesShown).Select(d =>
        {
            var label = d.Kind switch
            {
                DriverKind.History => "history",
                DriverKind.Event => $"{GameNames.Pretty(d.Source)} events x{d.Count}",
                DriverKind.Edict => $"edict {GameNames.Pretty(d.Source)}",
                DriverKind.Demand => $"demand {GameNames.Pretty(d.Source)}",
                _ => GameNames.Pretty(d.Source),
            };
            return $"{label} {d.Value.ToString("+0.0;-0.0", CultureInfo.CurrentCulture)}";
        }));
    }
}
