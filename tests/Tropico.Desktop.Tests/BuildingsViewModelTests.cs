using Tropico.Analysis;
using Tropico.Desktop.ViewModels;

namespace Tropico.Desktop.Tests;

public class BuildingsViewModelTests
{
    private static IslandSnapshot Snapshot() => new(
        "test", "t6", new BuildingSummary(12, new Dictionary<string, int>(), new Dictionary<string, int> { ["Built"] = 9, ["Damaged"] = 2, ["Unknown"] = 1 }),
        new PopulationSummary(1000, 200, 800, 0, 0), 1_000, 0, [], [], [], [])
    {
        ProductionData = new ProductionSnapshot(
            Classes:
            [
                new BuildingClassSummary("BP_T6Mine_C", 10, new Dictionary<string, int>(),
                    new Dictionary<string, int> { ["BP_T6WorkmodeProfitProtocol_C"] = 9, ["BP_T6WorkmodeOther_C"] = 1 },
                    new Dictionary<string, int> { ["Level5"] = 10 }, ["Coal", "Gold"], 0.5, 2),
                new BuildingClassSummary("BP_T6CountryHouse_C", 2, new Dictionary<string, int> { ["Damaged"] = 2 },
                    new Dictionary<string, int> { ["-"] = 2 }, new Dictionary<string, int> { ["-"] = 2 }, [], null, 0),
            ],
            Resources: [new ResourceProduction("Coal", 6, 1_200, 5_000, 2, 1, 33_409, 27_748)],
            Deposits: [new DepositSummary("Coal", 8, 4, 800_000, 6), new DepositSummary("Uranium", 8, 0, 160_000, 0)],
            FullOutputs: [new ProducerStock(1, "BP_T6Mine_C", "Coal", 1_000, 1_000), new ProducerStock(2, "BP_T6Mine_C", "Gold", 990, 1_000)],
            Producers: 10),
    };

    private static readonly Finding[] Findings =
    [
        new("a", Severity.Warning, Confidence.Verified, "cond") { Category = "Buildings" },
        new("b", Severity.Info, Confidence.Probable, "prod") { Category = "Production" },
        new("c", Severity.Info, Confidence.Probable, "eco") { Category = "Economy" },
    ];

    // digits only: the thousands separator depends on the machine culture
    private static string Digits(string text) => new(text.Where(char.IsDigit).ToArray());

    [Fact]
    public void Cards_CountBuildingsProducersFullOutputsAndProblems()
    {
        var vm = new BuildingsViewModel(Snapshot(), Findings);

        Assert.Equal("12", vm.Total);
        Assert.Equal("10", vm.Producers);
        Assert.Equal("2", vm.FullOutputs);
        Assert.Equal("2", vm.NotBuilt); // Damaged counts; Built and Unknown do not
        Assert.Equal(["cond", "prod"], vm.Findings.Select(f => f.Message));
    }

    [Fact]
    public void Classes_UseReadableNamesAndSummariseConditionWorkmodeAndOutputs()
    {
        var rows = new BuildingsViewModel(Snapshot(), Findings).Classes;

        Assert.Equal("Mine", rows[0].Name);
        Assert.Equal("all built", rows[0].Condition);
        Assert.Equal("Profit Protocol (+1 other)", rows[0].Workmode);
        Assert.Equal("Level5", rows[0].Budget);
        Assert.Equal("Coal, Gold", rows[0].Outputs);
        Assert.Contains("50", rows[0].Fill);

        Assert.Equal("Country House", rows[1].Name);
        Assert.Equal("2 Damaged", rows[1].Condition);
        Assert.Equal("-", rows[1].Outputs);
        Assert.Equal("-", rows[1].Fill);
    }

    [Fact]
    public void Resources_AndDeposits_FormatTheProductionTables()
    {
        var vm = new BuildingsViewModel(Snapshot(), Findings);

        var coal = vm.Resources[0];
        Assert.Equal("Coal", coal.Resource);
        Assert.Equal("1200", Digits(coal.Output));
        Assert.Equal("24", Digits(coal.Fill)); // 1,200 / 5,000
        Assert.Equal("2", coal.Full);
        Assert.Equal("33409", Digits(coal.IslandStock));

        Assert.Equal("4", vm.Deposits[0].InUse);
        Assert.Equal("800000", Digits(vm.Deposits[0].Amount));
        Assert.Equal("0", vm.Deposits[1].InUse); // Uranium: nothing produces it
    }

    [Fact]
    public void WithoutProductionData_ShowsEmptyTablesInsteadOfFailing()
    {
        var vm = new BuildingsViewModel(Snapshot() with { ProductionData = null }, []);

        Assert.Empty(vm.Classes);
        Assert.Empty(vm.Resources);
        Assert.Empty(vm.Deposits);
        Assert.Equal("0", vm.Producers);
    }

    [Theory]
    [InlineData("BP_T6WorkmodeProfitProtocol_C", "Profit Protocol")]
    [InlineData("BP_T6WorkmodeNormalOccupancy_C", "Normal Occupancy")]
    [InlineData("BP_T6WorkmodeBetterThanShack_C", "Better Than Shack")]
    [InlineData("-", "-")]
    public void Workmode_NamesAreReadable(string name, string expected) => Assert.Equal(expected, DisplayNames.Workmode(name));

    [Fact]
    public void Dominant_ShowsTheMostCommonValueAndHowManyOthersExist()
    {
        var mixed = new Dictionary<string, int> { ["a"] = 1, ["b"] = 5, ["c"] = 1 };

        Assert.Equal("B (+2 other)", BuildingsViewModel.Dominant(mixed, v => v.ToUpperInvariant()));
        Assert.Equal("a", BuildingsViewModel.Dominant(new Dictionary<string, int> { ["a"] = 3 }, v => v));
        Assert.Equal("-", BuildingsViewModel.Dominant(new Dictionary<string, int>(), v => v));
    }
}
