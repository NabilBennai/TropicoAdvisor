using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tropico.Analysis;
using Tropico.Localization;

namespace Tropico.Desktop.ViewModels;

public sealed record FactionRow(string Name, string Standing, bool IsNegative, string History, string Causes);

public sealed record EdictRow(string Name, string Active);

public sealed record ConstitutionRow(string Topic, string Option);

public sealed record DemandRow(string Name, string Status);

public sealed class PoliticsViewModel
{
    private const int CausesShown = 3;

    public PoliticsViewModel(IslandSnapshot snapshot, IReadOnlyList<Finding> findings, ILocalizer? localizer = null)
    {
        var loc = localizer ?? Localizer.English;
        var culture = loc.Culture;
        var politics = snapshot.Politics;

        Era = politics.Era is null ? "?" : loc.Term(new Term("era", AfterSeparator(politics.Era)));
        Constitution = politics.ConstitutionSigned ?? loc.Get("vm.notSigned");
        Elections = politics.MonthsUntilElections is { } months ? loc.Format("vm.inMonths", months) : "?";
        Research = politics.ResearchPoints is { } research ? research.ToString("N0", culture) : "?";
        VictoryPoints = politics.VictoryPoints is { } victory ? victory.ToString("N0", culture) : "?";

        Factions = politics.Factions.Select(f => new FactionRow(
            loc.Term(Term.Faction(f.Name)),
            f.KnownTotal.ToString("+0.0;-0.0;0.0", culture),
            f.KnownTotal < 0,
            f.HistoryNet is { } net ? loc.Format("vm.historyNet", net, f.HistoryPositive ?? 0, f.HistoryNegative ?? 0) : "-",
            Causes(f, loc))).ToList();
        Edicts = politics.Edicts.OrderBy(e => e.IsCustom).ThenByDescending(e => e.MonthsActive ?? 0)
            .Select(e => new EdictRow(
                e.IsCustom ? loc.Format("vm.customEdict", GameNames.Pretty(e.Name)) : GameNames.Pretty(e.Name),
                e.MonthsActive is { } m ? loc.Format("vm.monthsCount", m) : "-"))
            .ToList();
        ConstitutionChoices = politics.Constitution.Select(c => new ConstitutionRow(GameNames.Pretty(c.Topic), c.Option is null ? "-" : GameNames.Pretty(c.Option))).ToList();
        Demands = politics.Demands.OrderBy(d => d.Status, System.StringComparer.Ordinal).ThenBy(d => d.Name, System.StringComparer.Ordinal)
            .Select(d => new DemandRow(GameNames.Pretty(d.Name), d.Status is null ? "-" : loc.Term(new Term("demandStatus", d.Status)))).ToList();
        Landmarks = politics.Landmarks.Count == 0 ? loc.Get("vm.none") : string.Join(loc.Get("list.separator"), politics.Landmarks.Select(DisplayNames.Category));
        Findings = findings.Where(f => f.Category == "Politics").Select(f => new FindingViewModel(f, loc)).ToList();
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

    // "ET6PlayerEra::WorldWars" -> "WorldWars"
    private static string AfterSeparator(string name)
    {
        var separator = name.LastIndexOf("::", System.StringComparison.Ordinal);
        return separator < 0 ? name : name[(separator + 2)..];
    }

    // The largest effects first: "Mine events x4 -6.7; history -2.7".
    private static string Causes(FactionStanding faction, ILocalizer loc)
    {
        if (faction.Drivers.Count == 0) return "-";

        return string.Join("; ", faction.Drivers.OrderByDescending(d => System.Math.Abs(d.Value)).Take(CausesShown).Select(d =>
        {
            var label = d.Kind switch
            {
                DriverKind.History => loc.Get("driver.history"),
                DriverKind.Event => loc.Format("driver.event", GameNames.Pretty(d.Source), d.Count),
                DriverKind.Edict => loc.Format("driver.edict", GameNames.Pretty(d.Source)),
                DriverKind.Demand => loc.Format("driver.demand", GameNames.Pretty(d.Source)),
                _ => GameNames.Pretty(d.Source),
            };
            return $"{label} {d.Value.ToString("+0.0;-0.0", loc.Culture)}";
        }));
    }
}
