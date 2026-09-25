namespace Tropico.SaveParser.Tests;

[Trait("Category", "RealSave")]
public class T6PoliticsTests
{
    private static readonly Lazy<T6Politics> Politics = new(() => T6PoliticsReader.Read(T6SaveReader.Read(T6SaveReaderTests.FindSample())));

    private static T6Politics Data => Politics.Value;

    private static T6Faction Faction(string name) => Data.Factions.Single(f => f.Name == name);

    [Fact]
    public void Factions_AreTheTenGameFactions()
    {
        Assert.Equal(
            ["Capitalists", "Communists", "Conservatives", "Environmentalists", "Industrialists", "Intellectuals", "Militarists", "Religious", "Revolutionaries", "Royalist"],
            Data.Factions.Select(f => f.Name).Order());
    }

    [Fact]
    public void Faction_History_MatchesTheStoredAccounts()
    {
        var religious = Faction("Religious");

        Assert.Equal(22, religious.HistoryPositive);
        Assert.Equal(216, religious.HistoryNegative);
        Assert.Equal(-194, religious.HistoryNet);
        Assert.Equal(-7.76, religious.KnownTotal, 2);

        // the history modifier value is net / 25: an independent consistency check of the decoding
        var history = religious.Modifiers.Single(m => m.IsHistory);
        Assert.Equal(religious.HistoryNet!.Value / 25, history.Value!.Value, 2);
    }

    [Fact]
    public void Faction_Totals_SumEveryStoredModifierValue()
    {
        Assert.Equal(1.52, Faction("Revolutionaries").KnownTotal, 2);   // 5.52 history + 1.0 reward - 5.0 penalty
        Assert.Equal(-13.48, Faction("Capitalists").KnownTotal, 2);     // -3.48 history - 10 edict
        Assert.Equal(13.08, Faction("Communists").KnownTotal, 2);       // 8.08 history + 5 edict
        Assert.Equal(-8.22, Faction("Environmentalists").KnownTotal, 2);
    }

    [Fact]
    public void Modifiers_KeepTheirSource_SoTheDriversAreIdentifiable()
    {
        var edict = Faction("Capitalists").Modifiers.Single(m => m.IsEdictEffect);
        Assert.Equal(-10, edict.Value);
        Assert.Equal("BP_Edict_Food_for_the_People_C", edict.SourceClass);
        Assert.Equal("BP_Faction_Capitalists_C", edict.TargetClass);

        var mines = Faction("Environmentalists").Modifiers.Where(m => m.IsFleeting && m.SourceClass == "BP_T6Mine_C").ToList();
        Assert.Equal(4, mines.Count);
        Assert.Equal(-6.7, mines.Sum(m => m.Value!.Value), 2);
        Assert.All(mines, m => Assert.Equal(-5.0, m.StartValue)); // fleeting events decay from their start value

        Assert.Equal(6.7, Faction("Industrialists").Modifiers.Where(m => m.SourceClass == "BP_T6Mine_C").Sum(m => m.Value!.Value), 2);
    }

    [Fact]
    public void Modifiers_WithoutStoredValue_AreKeptAsUnknown()
    {
        var leader = Faction("Religious").Modifiers.Single(m => m.Kind == "T6StandingModifierDynamicLeader");

        Assert.Null(leader.Value);
        Assert.Contains(Faction("Religious").Modifiers, m => m.Kind == "T6StandingModifierDynCorruption" && m.Value is null);
    }

    [Fact]
    public void Edicts_ListActiveAndCustomEdictsWithTheirAge()
    {
        var edicts = Data.Edicts.ToDictionary(e => e.Name);

        Assert.Equal(5, Data.Edicts.Count);
        Assert.Equal(138, edicts["BP_Edict_Food_for_the_People_C"].MonthsActive);
        Assert.Equal(375, edicts["BP_T6EdictEmployeeOfTheMonth_C"].MonthsActive);
        Assert.All(Data.Edicts, e => Assert.True(e.IsActive));

        var custom = Assert.Single(Data.Edicts, e => e.IsCustom);
        Assert.Equal("BP_T6_NoShacksAllowed_C", custom.Name);
        Assert.Equal(417, custom.MonthsActive);
    }

    [Fact]
    public void Constitution_ListsOnlyTopicsWithASerializedOption()
    {
        Assert.Equal(["BP_T6ConstitutionTopicArmedForces_C", "BP_T6ConstitutionTopicVotingRights_C"], Data.Constitution.Select(c => c.Topic).Order());
        Assert.All(Data.Constitution, c => Assert.False(string.IsNullOrEmpty(c.ActiveOption)));
    }

    [Fact]
    public void Player_AndElections_ExposeTheStateOfTheMandate()
    {
        var player = Data.Player!;
        Assert.Equal("WorldWars", player.Era);
        Assert.True(player.ConstitutionSigned);
        Assert.Equal((9, 1934), (player.ConstitutionMonth!.Value, player.ConstitutionYear!.Value));
        Assert.Equal(7055.43, player.ResearchPoints!.Value, 2);
        Assert.Equal(39, player.VictoryPoints);
        Assert.Equal(["RegistanOfSamarkand", "HagiaSophia"], player.Landmarks);

        Assert.Equal(new T6Elections(120, true, 12512), Data.Elections);
    }

    [Fact]
    public void Demands_CarryTheirStatus()
    {
        var demands = Data.Demands.ToDictionary(d => d.Name);

        Assert.Equal("Rewarded", demands["BP_T6FactionDemand_REV_8_C"].Status);
        Assert.Equal("Abandoned", demands["BP_T6_SuperpowerDemand_Crown16_C"].Status);
        Assert.Equal(11062, demands["BP_T6FactionDemand_REV_8_C"].OfferedDay);
        Assert.All(Data.Demands, d => Assert.NotNull(d.Status));
    }
}

public class T6PoliticsLargeSaveTests
{
    [Trait("Category", "RealSave")]
    [Fact]
    public void Read_Isla_DecodesFactionsAndEdicts()
    {
        var politics = T6PoliticsReader.Read(T6SaveReader.Read(T6SaveReaderTests.FindSample("Trop6_Sav_Isla cuadrada Oct, 2069.t6sav")));

        Assert.NotEmpty(politics.Factions);
        Assert.All(politics.Factions, f => Assert.NotEmpty(f.Modifiers));
        Assert.NotNull(politics.Player);
        Assert.NotNull(politics.Elections);
        Assert.NotEmpty(politics.Edicts);
    }
}
