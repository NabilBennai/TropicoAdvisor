namespace Tropico.SaveParser.Tests;

/// <summary>Non-regression on a second, much larger save (57 MB decompressed) to check the readers are not tuned to one island.</summary>
public class T6LargeSaveTests
{
    private const string IslaName = "Trop6_Sav_Isla cuadrada Oct, 2069.t6sav";

    private static readonly Lazy<T6SaveFile> Isla = new(() => T6SaveReader.Read(T6SaveReaderTests.FindSample(IslaName)));

    private static T6SaveFile Save => Isla.Value;

    [Fact]
    public void Container_Isla_ParsesHeaderAndTables()
    {
        Assert.Equal("t6-#1290-win64-steam@dcffff2", Save.Header.Build);
        Assert.Equal("Isla cuadrada Oct, 2069", Save.Header.SaveName);
        Assert.Equal(0xD4, Save.Header.CompressedDataOffset); // header length differs from urss (0xDB): offset comes from the header
        Assert.Equal(57_331_094, Save.DecompressedData.Length);
        Assert.Equal(1613, Save.NameTable.Names.Count);
        Assert.Equal(52_552, Save.ObjectTable.Objects.Count);
        Assert.Equal(0x2DBB53, Save.ObjectTable.BlobBaseOffset);
    }

    [Fact]
    public void Properties_Isla_MatchReferenceStatusCounts()
    {
        var counts = Save.ObjectTable.Objects
            .Select(o => Save.ReadObject(o).Status)
            .GroupBy(s => s)
            .ToDictionary(g => g.Key, g => g.Count());

        // reference values from the Python decoder on the same save
        Assert.Equal(1448, counts.GetValueOrDefault(T6ObjectDataStatus.NoBlob));
        Assert.Equal(41_528, counts.GetValueOrDefault(T6ObjectDataStatus.Complete));
        Assert.Equal(2_055, counts.GetValueOrDefault(T6ObjectDataStatus.Resynchronized));
        Assert.Equal(7_513, counts.GetValueOrDefault(T6ObjectDataStatus.Partial));
        Assert.Equal(8, counts.GetValueOrDefault(T6ObjectDataStatus.Failed));
    }

    [Fact]
    public void Buildings_Isla_MatchReferenceCounts()
    {
        var buildings = T6BuildingReader.Read(Save);

        Assert.Equal(1104, buildings.Count);
        Assert.All(buildings, b => Assert.NotNull(b.Transform));

        var byClass = buildings.GroupBy(b => b.ClassName).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(143, byClass["BP_T6ModernApartment_C"]);
        Assert.Equal(102, byClass["BP_T6AutomatedMine_C"]);
        Assert.Equal(83, byClass["BP_Building_Teamster_C"]);

        var states = buildings.GroupBy(b => b.BuildingState).ToDictionary(g => g.Key ?? "(none)", g => g.Count());
        Assert.Equal(1055, states["ET6BuildingState::Built"]);
        Assert.Equal(41, states["(none)"]); // no construction site component
        Assert.Equal(5, states["ET6BuildingState::Rubble"]);
        Assert.Equal(2, states["ET6BuildingState::Damaged"]);
        Assert.Equal(1, states["ET6BuildingState::Broken"]);
    }

    [Fact]
    public void Statistics_Isla_MatchReferenceValues()
    {
        var stats = T6IslandStatisticsReader.Read(Save);

        Assert.Equal(new T6PopulationCounters(6203, 976, 4595, 445, 1123, 177, 387, 4004, 2199), stats.Population);
        Assert.Equal(5846057.5, stats.Treasury);
        Assert.Equal(47650.0, stats.SwissBank);
        Assert.Equal(2_703_956, stats.YearlyFinances.TotalRevenue);
        Assert.Equal(2_069_390, stats.YearlyFinances.TotalExpenses);
    }

    [Fact]
    public void TradeEconomy_Isla_IsConsistentWithStatistics()
    {
        var economy = T6TradeEconomyReader.Read(Save);
        var stats = T6IslandStatisticsReader.Read(Save);

        // the calendar month index is the X axis of the history samples (verified on both saves)
        Assert.Equal(stats.LastSample("TreasuryHistory")!.X, economy.Calendar!.MonthIndex);
        Assert.NotEmpty(economy.Goods);
        Assert.NotEmpty(economy.RouteOffers);
        Assert.NotEmpty(economy.Stocks);
        Assert.NotEmpty(economy.ClassEconomies);
        Assert.DoesNotContain(economy.Goods, g => g.Resource == "?");
    }
}
