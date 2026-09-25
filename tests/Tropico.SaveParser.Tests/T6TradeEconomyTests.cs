namespace Tropico.SaveParser.Tests;

[Trait("Category", "RealSave")]
public class T6TradeEconomyTests
{
    private static readonly Lazy<T6TradeEconomy> Economy = new(() => T6TradeEconomyReader.Read(T6SaveReader.Read(T6SaveReaderTests.FindSample())));

    private static T6TradeEconomy Data => Economy.Value;

    [Fact]
    public void Calendar_MonthIndexMatchesHistoryAxis()
    {
        Assert.Equal(new T6Calendar(1934, 9, 5, 12515), Data.Calendar);
        Assert.Equal(417, Data.Calendar!.MonthIndex); // X of the last TreasuryHistory sample
    }

    [Fact]
    public void Goods_Gold_ExposesPriceAndTradeVolumes()
    {
        Assert.Equal(56, Data.Goods.Count);

        var gold = Data.Goods.Single(g => g.Resource == "Gold");
        Assert.Contains("Minerals", gold.Categories);
        Assert.Equal(48, gold.PriceHistory.Count);
        Assert.Equal(8.9658, gold.PriceHistory[0], 3);
        Assert.Equal([0, 1789, 480, 651, 0, 502, 0, 0, 0, 0, 0, 1217], gold.ExportVolumes);
        Assert.Equal(4639, gold.ExportedLast12Months);
        Assert.Equal(0, gold.ImportedLast12Months);
        Assert.Equal(gold.PriceHistory[^1], gold.CurrentPrice);
    }

    [Fact]
    public void RouteOffers_SplitImportsAndExportsByPartner()
    {
        Assert.Equal(114, Data.RouteOffers.Count);
        Assert.Equal(76, Data.RouteOffers.Count(o => o.IsImport));
        Assert.Equal(38, Data.RouteOffers.Count(o => !o.IsImport));

        var byPartner = Data.RouteOffers.GroupBy(o => o.Partner).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(43, byPartner["Smugglers"]);
        Assert.Equal(32, byPartner["TheCrown"]);
        Assert.Equal(10, byPartner["DominicanRepublic"]);
        Assert.Equal(9, byPartner["Mexico"]);

        var goldRoute = Data.RouteOffers.Single(o => o.ObjectIndex == 7378);
        Assert.Equal("Gold", goldRoute.Resource);
        Assert.False(goldRoute.IsImport); // bIsImport absent = default (export)
        Assert.Equal(1800, goldRoute.VolumeGoal);
        Assert.Equal("Neutral", goldRoute.RequiredStandingLevel);
    }

    [Fact]
    public void Contracts_AreResolvedToTheirRoute()
    {
        Assert.Equal(24, Data.Contracts.Count);
        var reasons = Data.Contracts.GroupBy(c => c.EndReason).ToDictionary(g => g.Key ?? "", g => g.Count());
        Assert.Equal(20, reasons["Fulfilled"]);
        Assert.Equal(3, reasons["InactiveByEra"]);
        Assert.Equal(1, reasons["Abandoned"]);

        Assert.DoesNotContain(Data.Contracts, c => c.Resource == "?");
        Assert.Contains(Data.Contracts, c => c is { Resource: "Coal", IsImport: true, Volume: > 0 });
    }

    [Fact]
    public void Stocks_AreSummedPerResource_LargestFirst()
    {
        var coal = Data.Stocks[0];
        Assert.Equal("Coal", coal.Resource);
        Assert.Equal(33409.1, coal.Total, 0);
        Assert.Equal(19, coal.StockCount);
        Assert.Equal(Data.Stocks.OrderByDescending(s => s.Total), Data.Stocks);
    }

    [Fact]
    public void ClassEconomies_AggregateByBuildingClass()
    {
        var byClass = Data.ClassEconomies.ToDictionary(e => e.ClassName);

        Assert.Equal(new T6BuildingClassEconomy("BP_T6CountryHouse_C", 28504, 0, 8160), byClass["BP_T6CountryHouse_C"]);
        Assert.Equal(new T6BuildingClassEconomy("BP_T6Mine_C", 0, 21000, 6300), byClass["BP_T6Mine_C"]);
        Assert.Equal(-27300, byClass["BP_T6Mine_C"].Net);
        Assert.Equal(54170, Data.ClassEconomies.Sum(e => e.FeesAndRents));
        Assert.Equal(111432, Data.ClassEconomies.Sum(e => e.Wages));
        Assert.Equal(41760, Data.ClassEconomies.Sum(e => e.Upkeep));
        Assert.Equal("BP_T6Mine_C", Data.ClassEconomies[0].ClassName); // ordered by Net, worst first
    }
}
