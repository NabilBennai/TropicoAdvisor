namespace Tropico.SaveParser.Tests;

public class T6IslandReadersTests
{
    private static readonly Lazy<T6SaveFile> Urss = new(() => T6SaveReader.Read(T6SaveReaderTests.FindSample()));

    private static T6SaveFile Save => Urss.Value;

    [Fact]
    public void Buildings_ReferenceSave_CountsInstancesNotTextOccurrences()
    {
        var buildings = T6BuildingReader.Read(Save);

        Assert.Equal(297, buildings.Count);

        // cross-check: every building owns exactly one construction site component (297 in the save)
        var sites = Save.ObjectTable.Objects.Count(o => o.ShortName == "T6ConstructionSiteComponent");
        Assert.Equal(sites, buildings.Count);
        Assert.All(buildings, b => Assert.Equal("ET6BuildingState::Built", b.BuildingState));
        Assert.All(buildings, b => Assert.NotNull(b.Transform));

        var byClass = buildings.GroupBy(b => b.ClassName).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(85, byClass["BP_T6CountryHouse_C"]);
        Assert.Equal(60, byClass["BP_T6Bunkhouse_C"]);
        Assert.Equal(35, byClass["BP_T6Mine_C"]);
        Assert.Equal(20, byClass["BP_Building_Plantation_C"]);

        // ships share the path prefix (31 text occurrences) but are not buildings
        Assert.DoesNotContain("BP_T6Freighter_C", byClass.Keys);
        Assert.Equal(31, Save.ObjectTable.Objects.Count(o => o.ShortName == "BP_T6Freighter_C"));
    }

    [Fact]
    public void Buildings_Bunkhouse_ExposesTransformWorkmodeAndComponents()
    {
        var bunkhouse = T6BuildingReader.Read(Save).Single(b => b.ObjectIndex == 1458);

        Assert.Equal("BP_T6Bunkhouse_C", bunkhouse.ClassName);
        Assert.Equal(82500f, bunkhouse.Transform!.X);
        Assert.Equal(163500f, bunkhouse.Transform.Y);
        Assert.Equal("BP_T6WorkmodeNormalOccupancy_C", bunkhouse.Workmode);
        Assert.Contains("T6ConstructionSiteComponent", bunkhouse.Components);
        Assert.Null(bunkhouse.BudgetLevel); // default, not serialized
    }

    [Fact]
    public void Statistics_ReferenceSave_ReadsPopulationCounters()
    {
        var stats = T6IslandStatisticsReader.Read(Save);

        Assert.Equal(7791, stats.CollectorObjectIndex);
        Assert.Equal(new T6PopulationCounters(1313, 273, 952, 1, 21, 4, 773, 519, 794), stats.Population);

        var unemployed = stats.LastSample("UnemployedHistory")!;
        Assert.Equal(417, unemployed.X);
        Assert.Equal([59.0, 72.0, 1.0], unemployed.Y);
    }

    [Fact]
    public void Statistics_ReferenceSave_ReadsTreasuryAndFinances()
    {
        var stats = T6IslandStatisticsReader.Read(Save);

        Assert.Equal(61, stats.Histories["TreasuryHistory"].Count);
        Assert.Equal(1453750.25, stats.Treasury);
        Assert.Equal(0.0, stats.SwissBank);

        Assert.Equal(12, stats.MonthBalance.Count);
        Assert.Equal(-8013.25, stats.MonthBalance[0]);
        Assert.Equal(141536.09, stats.MonthBalance.Sum(), 1);

        // reference values from the Python decoder (uncertain semantics, see T6YearlyFinances)
        var finances = stats.YearlyFinances;
        Assert.Equal(294_353, finances.TotalRevenue);
        Assert.Equal(237_658, finances.RevenueByCategory["Exports"]);
        Assert.Equal(6_456, finances.RevenueByCategory["Fees"]);
        Assert.Equal(47_714, finances.RevenueByCategory["Rents"]);
        Assert.Equal(165_044, finances.TotalExpenses);
        Assert.Equal(finances.RevenueByCategory["Exports"], finances.ExportRevenueByResource.Values.Sum());
    }

    [Fact]
    public void AgentCensus_ReferenceSave_MatchesLifeStateCounts()
    {
        var census = T6AgentCensusReader.Read(Save);

        Assert.Equal(1412, census.AgentObjects);
        Assert.Equal(953, census.LifeState["ET6AgentLifeState::Adult"]);
        Assert.Equal(1, census.LifeState["ET6AgentLifeState::Retired"]);
        Assert.Equal(96, census.LifeState["ET6AgentLifeState::Toddler"]);
        Assert.Equal(89, census.LifeState["ET6AgentLifeState::Dead"]);
        Assert.Equal(273, census.LifeState[T6AgentCensus.DefaultValue]); // default state (children), not serialized

        // independent cross-check against the game's own counters
        var counters = T6IslandStatisticsReader.Read(Save).Population;
        Assert.Equal(counters.Children, census.LifeState[T6AgentCensus.DefaultValue]);
        Assert.Equal(counters.Retired, census.LifeState["ET6AgentLifeState::Retired"]);
        Assert.InRange(census.LifeState["ET6AgentLifeState::Adult"] - counters.Adults!.Value, 0, 1);
    }
}
