using Tropico.SaveParser;

namespace Tropico.Analysis.Tests;

public class GameNamesTests
{
    [Theory]
    [InlineData("BP_Edict_Food_for_the_People_C", "Food for the People")]
    [InlineData("BP_T6EdictEmployeeOfTheMonth_C", "Employee Of The Month")]
    [InlineData("BP_T6_NoShacksAllowed_C", "No Shacks Allowed")]
    [InlineData("BP_T6Mine_C", "Mine")]
    [InlineData("BP_T6PileOfBS_C", "Pile Of BS")]
    [InlineData("BP_T6ConstitutionTopicVotingRights_C", "Voting Rights")]
    [InlineData("BP_T6ConstitutionHealthcare_C", "Healthcare")]
    [InlineData("BP_T6ConstitutionOptionAllCitizensVote_C", "All Citizens Vote")]
    [InlineData("BP_T6ConstitutionOptionMilitia_C", "Militia")]
    public void Pretty_RemovesTechnicalPrefixesAndSplitsWords(string name, string expected) => Assert.Equal(expected, GameNames.Pretty(name));
}

public class PoliticsRulesTests
{
    private static FactionStanding Faction(string name, double total, params FactionDriver[] drivers) =>
        new(name, total, null, null, null, drivers);

    private static IslandSnapshot Snapshot(PoliticsSnapshot politics) => new(
        "test", "t6-test",
        new BuildingSummary(10, new Dictionary<string, int>(), new Dictionary<string, int> { ["Built"] = 10 }),
        new PopulationSummary(1000, 200, 800, 0, 0), 100_000, 0, [], [], [], [])
    {
        PoliticsData = politics,
    };

    private static PoliticsSnapshot Politics(int? elections = 120, params FactionStanding[] factions) =>
        PoliticsSnapshot.Empty with { MonthsUntilElections = elections, Factions = factions, Edicts = [new EdictInfo("BP_Edict_Food_for_the_People_C", 138, false)] };

    private static IReadOnlyList<Finding> Run(IslandSnapshot s) => new IslandAnalyzer().Analyze(s).Findings;

    private static Finding? Find(IslandSnapshot s, string code) => Run(s).SingleOrDefault(f => f.Code == code);

    [Fact]
    public void FactionStanding_EdictCause_IsAWarningBelowTheThreshold()
    {
        var snapshot = Snapshot(Politics(120,
            Faction("Capitalists", -13.5, new FactionDriver(DriverKind.Edict, "BP_Edict_Food_for_the_People_C", -10), new FactionDriver(DriverKind.History, "Capitalists", -3.5))));

        var finding = Find(snapshot, "politics.faction-negative.Capitalists")!;

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("Politics", finding.Category);
        Assert.Contains("-13.5", finding.Message);
        Assert.Equal(["edict Food for the People: -10.0", "history of past actions: -3.5"], finding.Evidence);
        Assert.Contains("edict Food for the People", finding.Suggestion);
    }

    [Fact]
    public void FactionStanding_BuildingEvents_AreNamedAsTheCause()
    {
        var snapshot = Snapshot(Politics(120,
            Faction("Environmentalists", -8.2, new FactionDriver(DriverKind.Event, "BP_T6Mine_C", -6.7, 4), new FactionDriver(DriverKind.Event, "BP_T6PileOfBS_C", 1.1, 2))));

        var finding = Find(snapshot, "politics.faction-negative.Environmentalists")!;

        Assert.Equal(Severity.Info, finding.Severity); // above the -10 warning threshold
        Assert.Contains("4 recent event(s) from Mine buildings", finding.Suggestion);
        Assert.Contains("Mine events x4: -6.7", finding.Evidence);
        Assert.Contains("Pile Of BS events x2: +1.1", finding.Evidence);
    }

    [Fact]
    public void FactionStanding_HistoryCause_QuotesTheEventCountsAndAHedgedConcern()
    {
        var religious = new FactionStanding("Religious", -7.8, 22, 216, -194, [new FactionDriver(DriverKind.History, "Religious", -7.8)]);

        var finding = Find(Snapshot(Politics(120, religious)), "politics.faction-negative.Religious")!;

        Assert.Contains("216 negative against 22 positive", finding.Suggestion);
        Assert.Contains("usually cares about religion", finding.Suggestion);
    }

    [Fact]
    public void FactionStanding_PositiveOrZeroFactions_AreSilent()
    {
        var snapshot = Snapshot(Politics(120, Faction("Communists", 13, new FactionDriver(DriverKind.Edict, "BP_Edict_X_C", 5)), Faction("Militarists", 0)));

        Assert.DoesNotContain(Run(snapshot), f => f.Code.StartsWith("politics.faction-negative"));
    }

    [Fact]
    public void EdictTradeOff_ListsWhoIsDispleasedAndPleased()
    {
        var snapshot = Snapshot(Politics(120,
            Faction("Capitalists", -13.5, new FactionDriver(DriverKind.Edict, "BP_Edict_Food_for_the_People_C", -10)),
            Faction("Communists", 5, new FactionDriver(DriverKind.Edict, "BP_Edict_Food_for_the_People_C", 5))));

        var finding = Find(snapshot, "politics.edict.BP_Edict_Food_for_the_People_C")!;

        Assert.Equal("The edict Food for the People displeases Capitalists and pleases Communists.", finding.Message);
        Assert.Equal(["Capitalists: -10.0", "Communists: +5.0", "Active for 138 months"], finding.Evidence);
    }

    [Fact]
    public void EdictTradeOff_IgnoresSmallEffects()
    {
        var snapshot = Snapshot(Politics(120, Faction("Capitalists", -2, new FactionDriver(DriverKind.Edict, "BP_Edict_X_C", -2))));

        Assert.DoesNotContain(Run(snapshot), f => f.Code.StartsWith("politics.edict."));
    }

    [Fact]
    public void Elections_SoonWithDispleasedFactions_IsAWarning()
    {
        var soon = Snapshot(Politics(6, Faction("Capitalists", -4), Faction("Communists", 3)));

        var finding = Find(soon, "politics.elections")!;

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("Elections in 6 months; factions below zero: Capitalists.", finding.Message);
        Assert.NotNull(finding.Suggestion);
    }

    [Fact]
    public void Elections_SoonWithoutProblems_IsInfo_AndFarAwayIsSilent()
    {
        Assert.Equal(Severity.Info, Find(Snapshot(Politics(12, Faction("Communists", 3))), "politics.elections")!.Severity);
        Assert.Null(Find(Snapshot(Politics(120, Faction("Capitalists", -4))), "politics.elections"));
        Assert.Null(Find(Snapshot(Politics(null)), "politics.elections"));
    }
}

public class PoliticsSnapshotTests
{
    private static readonly Lazy<T6SaveFile> Urss = new(() => T6SaveReader.Read(IslandSnapshotBuilderTests.FindSample()));

    [Fact]
    public void Build_ReferenceSave_ExposesFactionsOrderedFromTheMostDispleased()
    {
        var politics = IslandSnapshotBuilder.Build(Urss.Value).Politics;

        Assert.Equal(10, politics.Factions.Count);
        Assert.Equal(["Capitalists", "Environmentalists", "Religious"], politics.Factions.Take(3).Select(f => f.Name));
        Assert.Equal(politics.Factions.OrderBy(f => f.KnownTotal), politics.Factions);

        var capitalists = politics.Factions[0];
        Assert.Equal(-13.48, capitalists.KnownTotal, 2);
        Assert.Contains(capitalists.Drivers, d => d is { Kind: DriverKind.Edict, Source: "BP_Edict_Food_for_the_People_C", Value: -10 });
        Assert.Contains(capitalists.Drivers, d => d.Kind == DriverKind.History);

        var environmentalists = politics.Factions[1];
        var mines = environmentalists.Drivers.Single(d => d is { Kind: DriverKind.Event, Source: "BP_T6Mine_C" });
        Assert.Equal(-6.7, mines.Value, 2);
        Assert.Equal(4, mines.Count);
    }

    [Fact]
    public void Build_ReferenceSave_ExposesTheMandateAndEdicts()
    {
        var politics = IslandSnapshotBuilder.Build(Urss.Value).Politics;

        Assert.Equal("WorldWars", politics.Era);
        Assert.Equal("Sep 1934", politics.ConstitutionSigned);
        Assert.Equal(120, politics.MonthsUntilElections);
        Assert.Equal(39, politics.VictoryPoints);
        Assert.Equal(2, politics.Landmarks.Count);
        Assert.Equal(5, politics.Edicts.Count);
        Assert.Contains(politics.Edicts, e => e is { Name: "BP_T6_NoShacksAllowed_C", IsCustom: true, MonthsActive: 417 });
        Assert.Equal(2, politics.Constitution.Count);
    }

    [Fact]
    public void Analyze_ReferenceSave_ProducesPoliticalSuggestions()
    {
        var findings = new IslandAnalyzer().Analyze(Urss.Value).Findings;

        Assert.Equal(Severity.Warning, Assert.Single(findings, f => f.Code == "politics.faction-negative.Capitalists").Severity);
        Assert.Contains("Mine buildings", Assert.Single(findings, f => f.Code == "politics.faction-negative.Environmentalists").Suggestion);
        Assert.Contains("history", Assert.Single(findings, f => f.Code == "politics.faction-negative.Religious").Suggestion);
        Assert.DoesNotContain(findings, f => f.Code == "politics.faction-negative.Communists");
        Assert.Contains("displeases Capitalists and pleases Communists", Assert.Single(findings, f => f.Code.StartsWith("politics.edict.")).Message);
        Assert.DoesNotContain(findings, f => f.Code == "politics.elections"); // elections are 120 months away
    }
}
