using Tropico.Analysis;
using Tropico.Desktop.ViewModels;

namespace Tropico.Desktop.Tests;

public class PoliticsViewModelTests
{
    private static IslandSnapshot Snapshot() => new(
        "test", "t6", new BuildingSummary(10, new Dictionary<string, int>(), new Dictionary<string, int>()),
        new PopulationSummary(1000, 200, 800, 0, 0), 1_000, 0, [], [], [], [])
    {
        PoliticsData = new PoliticsSnapshot(
            "ET6PlayerEra::WorldWars", "Sep 1934", 120, 7055.4, 39, ["ET6Landmark::HagiaSophia"],
            Factions:
            [
                new FactionStanding("Capitalists", -13.48, 10, 20, -10,
                [
                    new FactionDriver(DriverKind.Edict, "BP_Edict_Food_for_the_People_C", -10),
                    new FactionDriver(DriverKind.History, "Capitalists", -3.48),
                ]),
                new FactionStanding("Communists", 13.08, null, null, null, [new FactionDriver(DriverKind.History, "Communists", 8.08)]),
                new FactionStanding("Religious", 0, null, null, null, []),
            ],
            Edicts: [new EdictInfo("BP_T6_NoShacksAllowed_C", 417, true), new EdictInfo("BP_Edict_Food_for_the_People_C", 138, false), new EdictInfo("BP_T6EdictUrbanDevelopment_C", 150, false)],
            Constitution: [new ConstitutionInfo("BP_T6ConstitutionTopicVotingRights_C", "BP_T6ConstitutionOptionAllCitizensVote_C"), new ConstitutionInfo("BP_T6ConstitutionTopicEcology_C", null)],
            Demands: [new DemandInfo("BP_T6FactionDemand_REV_8_C", "Rewarded"), new DemandInfo("BP_T6_SuperpowerDemand_Crown16_C", "Abandoned")]),
    };

    private static readonly Finding[] Findings =
    [
        new("a", Severity.Warning, Confidence.Probable, "pol") { Category = "Politics" },
        new("b", Severity.Info, Confidence.Probable, "eco") { Category = "Economy" },
    ];

    [Fact]
    public void Cards_ShowTheStateOfTheMandate()
    {
        var vm = new PoliticsViewModel(Snapshot(), Findings);

        Assert.Equal("World Wars", vm.Era);
        Assert.Equal("Sep 1934", vm.Constitution);
        Assert.Equal("in 120 months", vm.Elections);
        Assert.Equal("39", vm.VictoryPoints);
        Assert.Equal("Hagia Sophia", vm.Landmarks);
        Assert.Equal(["pol"], vm.Findings.Select(f => f.Message));
    }

    [Fact]
    public void Factions_ShowSignedStandingHistoryAndMainCauses()
    {
        var rows = new PoliticsViewModel(Snapshot(), Findings).Factions;

        Assert.Equal("Capitalists", rows[0].Name);
        Assert.Equal("-13.5", rows[0].Standing.Replace(',', '.'));
        Assert.True(rows[0].IsNegative);
        Assert.Contains("(10 +, 20 -)", rows[0].History); // positive events, then negative events
        Assert.StartsWith("edict Food for the People -10", rows[0].Causes.Replace(',', '.')); // largest effect first
        Assert.Contains("history of past actions -3.5", rows[0].Causes.Replace(',', '.'));

        Assert.False(rows[1].IsNegative);
        Assert.StartsWith("+13", rows[1].Standing);
        Assert.Equal("-", rows[1].History); // no history stored
        Assert.Equal("-", rows[2].Causes);
        Assert.False(rows[2].IsNegative); // zero is not negative
    }

    [Fact]
    public void Edicts_ListRegularOnesByAge_AndMarkCustomOnes()
    {
        var rows = new PoliticsViewModel(Snapshot(), Findings).Edicts;

        Assert.Equal(["Urban Development", "Food for the People", "No Shacks Allowed (custom)"], rows.Select(r => r.Name));
        Assert.Contains("150", rows[0].Active);
    }

    [Fact]
    public void ConstitutionAndDemands_UseReadableNames()
    {
        var vm = new PoliticsViewModel(Snapshot(), Findings);

        Assert.Equal(("Voting Rights", "All Citizens Vote"), (vm.ConstitutionChoices[0].Topic, vm.ConstitutionChoices[0].Option));
        Assert.Equal("-", vm.ConstitutionChoices[1].Option);
        Assert.Equal(["Abandoned", "Rewarded"], vm.Demands.Select(d => d.Status));
        Assert.Equal("Superpower Demand Crown16", vm.Demands[0].Name);
    }

    [Fact]
    public void WithoutPoliticalData_ShowsPlaceholders()
    {
        var vm = new PoliticsViewModel(Snapshot() with { PoliticsData = null }, []);

        Assert.Equal("?", vm.Era);
        Assert.Equal("not signed", vm.Constitution);
        Assert.Equal("?", vm.Elections);
        Assert.Empty(vm.Factions);
        Assert.Equal("none", vm.Landmarks);
    }
}
