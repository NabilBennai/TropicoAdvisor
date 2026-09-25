using Tropico.SaveParser;

namespace Tropico.Analysis.Tests;

public class ProductionSnapshotTests
{
    private static readonly Lazy<ProductionSnapshot> Urss = new(() =>
        IslandSnapshotBuilder.Build(T6SaveReader.Read(IslandSnapshotBuilderTests.FindSample())).Production);

    [Fact]
    public void Build_ReferenceSave_SummarisesBuildingClasses()
    {
        var mines = Urss.Value.Classes.Single(c => c.ClassName == "BP_T6Mine_C");

        Assert.Equal(35, mines.Count);
        Assert.Empty(mines.States);
        Assert.Equal(35, mines.Workmodes["BP_T6WorkmodeProfitProtocol_C"]);
        Assert.Equal(35, mines.BudgetLevels["Level5"]);
        Assert.Equal(["Coal", "Gold", "Iron"], mines.Outputs);
        Assert.Equal(5, mines.FullOutputs);
        Assert.InRange(mines.AverageOutputFill!.Value, 0, 1);

        var houses = Urss.Value.Classes.Single(c => c.ClassName == "BP_T6CountryHouse_C");
        Assert.Equal(85, houses.Count);
        Assert.Empty(houses.Outputs);
        Assert.Null(houses.AverageOutputFill);
        Assert.Equal(85, houses.BudgetLevels["-"]); // houses have no budget level
    }

    [Fact]
    public void Build_ReferenceSave_FindsTheFullOutputStocks()
    {
        var full = Urss.Value.FullOutputs;

        Assert.Equal(5, full.Count);
        Assert.Equal(["Coal", "Coal", "Coal", "Gold", "Iron"], full.Select(o => o.Resource).Order());
        Assert.All(full, o => Assert.Equal("BP_T6Mine_C", o.ClassName));
        Assert.All(full, o => Assert.True(o.Fill >= ProducerStock.FullThreshold));
        Assert.Equal(full.OrderByDescending(o => o.Fill), full);
    }

    [Fact]
    public void Build_ReferenceSave_SummarisesEachProducedResource()
    {
        var coal = Urss.Value.Resources.Single(r => r.Resource == "Coal");

        Assert.Equal(14, coal.Producers);
        Assert.Equal(3, coal.FullProducers);
        Assert.Equal(33_409, coal.IslandStock, 0);
        Assert.Equal(27_748, coal.ExportedLast12Months);
        Assert.True(coal.OutputCapacity >= coal.OutputStock);
        Assert.Equal(35, Urss.Value.Resources.Where(r => r.Resource is "Coal" or "Gold" or "Iron").Sum(r => r.Producers));
    }

    [Fact]
    public void Build_ReferenceSave_EstimatesTappedDeposits()
    {
        var byResource = Urss.Value.Deposits.ToDictionary(d => d.Resource);

        Assert.Equal(new DepositSummary("Coal", 8, 4, 800_000, 14), byResource["Coal"]);
        Assert.Equal(4, byResource["Gold"].Tapped);
        Assert.Equal(2, byResource["Iron"].Tapped);
        Assert.Equal(1, byResource["Fish"].Tapped);
        Assert.Equal(0, byResource["Uranium"].Producers); // nothing mines it in this era
        Assert.Equal(0, byResource["Uranium"].Tapped);
    }

    [Fact]
    public void Analyze_ReferenceSave_ReportsTheBlockedMines()
    {
        var findings = new IslandAnalyzer().Analyze(T6SaveReader.Read(IslandSnapshotBuilderTests.FindSample())).Findings;

        var coal = Assert.Single(findings, f => f.Code == "production.full-output.Coal");
        Assert.Equal(Severity.Warning, coal.Severity);
        Assert.Equal("Production", coal.Category);
        Assert.Equal(Severity.Info, Assert.Single(findings, f => f.Code == "production.full-output.Gold").Severity);
        Assert.Contains(findings, f => f.Code == "production.untapped-deposits.Coal");
        Assert.DoesNotContain(findings, f => f.Code.StartsWith("production.untapped-deposits.Uranium")); // no producer: not an expansion lead
        Assert.Equal(Confidence.Uncertain, Assert.Single(findings, f => f.Code == "production.budget-uniform").Confidence);
    }
}

public class ProductionRulesTests
{
    private static ProducerStock Full(string resource, int index = 1, string className = "BP_Mine_C") => new(index, className, resource, 990, 1000);

    private static IslandSnapshot Snapshot(ProductionSnapshot production, long wages = 100, long expenses = 120) => new(
        "test", "t6-test",
        new BuildingSummary(10, new Dictionary<string, int>(), new Dictionary<string, int> { ["Built"] = 10 }),
        new PopulationSummary(1000, 200, 800, 0, 0), 100_000, 0, [], [], [], [],
        new EconomySnapshot(1, [], [], expenses * 2, expenses, wages, 0, 0, 0, new Dictionary<string, long>()))
    {
        ProductionData = production,
    };

    private static ProductionSnapshot Production(
        ProducerStock[]? full = null, ResourceProduction[]? resources = null, DepositSummary[]? deposits = null, BuildingClassSummary[]? classes = null) =>
        new(classes ?? [], resources ?? [], deposits ?? [], full ?? [], 10);

    private static ResourceProduction Resource(string name, int producers, int full = 0) => new(name, producers, 500, 1000, full, 0, 30_000, 5_000);

    private static IReadOnlyList<Finding> Run(IslandSnapshot s) => new IslandAnalyzer().Analyze(s).Findings;

    [Fact]
    public void FullOutput_OneProducerOfMany_IsInfo_ManyIsWarning()
    {
        var one = Run(Snapshot(Production([Full("Gold")], [Resource("Gold", 12, 1)]))).Single(f => f.Code == "production.full-output.Gold");
        var three = Run(Snapshot(Production([Full("Coal", 1), Full("Coal", 2), Full("Coal", 3)], [Resource("Coal", 14, 3)]))).Single(f => f.Code == "production.full-output.Coal");

        Assert.Equal(Severity.Info, one.Severity);
        Assert.Equal(Severity.Warning, three.Severity);
        Assert.Equal("3 Coal producer(s) have a full output storage.", three.Message);
        Assert.Contains("Island stock: 30,000", three.Evidence);
        Assert.Contains("Exported in the last 12 months: 5,000", three.Evidence);
        Assert.Contains("stops", three.Suggestion);
    }

    [Fact]
    public void FullOutput_ALargeShareOfProducers_IsWarning()
    {
        var finding = Run(Snapshot(Production([Full("Rum", 1), Full("Rum", 2)], [Resource("Rum", 4, 2)]))).Single(f => f.Code == "production.full-output.Rum");

        Assert.Equal(Severity.Warning, finding.Severity); // 2 of 4 = 50 %
    }

    [Fact]
    public void FullOutput_NothingFull_IsSilent() =>
        Assert.DoesNotContain(Run(Snapshot(Production(resources: [Resource("Coal", 14)]))), f => f.Code.StartsWith("production.full-output"));

    [Fact]
    public void ProducerStock_FullnessUsesTheThreshold()
    {
        Assert.True(new ProducerStock(1, "X", "Coal", 900, 1000).IsFull);
        Assert.False(new ProducerStock(1, "X", "Coal", 899, 1000).IsFull);
        Assert.False(new ProducerStock(1, "X", "Coal", 5, 0).IsFull); // no capacity: never "full"
        Assert.Equal(0.5, new ProducerStock(1, "X", "Coal", 500, 1000).Fill);
    }

    [Fact]
    public void UntappedDeposits_OnlyForProducedResources()
    {
        var findings = Run(Snapshot(Production(deposits:
        [
            new DepositSummary("Coal", 8, 4, 800_000, 14),   // produced, half unused
            new DepositSummary("Gold", 8, 8, 240_000, 12),   // all used
            new DepositSummary("Uranium", 8, 0, 160_000, 0), // not produced at all: not a lead
        ]))).Where(f => f.Code.StartsWith("production.untapped-deposits.")).ToList();

        var coal = Assert.Single(findings);
        Assert.Equal("production.untapped-deposits.Coal", coal.Code);
        Assert.Equal(Confidence.Uncertain, coal.Confidence);
        Assert.Contains("4 of 8 Coal", coal.Message);
    }

    private static BuildingClassSummary ClassWithLevels(string name, params (string Level, int Count)[] levels) =>
        new(name, levels.Sum(l => l.Count), new Dictionary<string, int>(), new Dictionary<string, int>(),
            levels.ToDictionary(l => l.Level, l => l.Count), [], null, 0);

    [Fact]
    public void BudgetLevel_DominantLevelWithHighWages_IsAnUncertainLead()
    {
        var snapshot = Snapshot(Production(classes: [ClassWithLevels("A", ("Level5", 100), ("Level4", 5)), ClassWithLevels("Houses", ("-", 80))]), wages: 80, expenses: 100);

        var finding = Run(snapshot).Single(f => f.Code == "production.budget-uniform");

        Assert.Equal(Confidence.Uncertain, finding.Confidence);
        Assert.Equal("100 of 105 workplaces use the same budget level (Level5) while wages are 80 % of expenses.", finding.Message);
    }

    [Fact]
    public void BudgetLevel_IsSilentWhenLevelsAreMixedWagesAreLowOrTooFewBuildings()
    {
        Assert.DoesNotContain(Run(Snapshot(Production(classes: [ClassWithLevels("A", ("Level5", 50), ("Level3", 50))]), wages: 80)), f => f.Code == "production.budget-uniform");
        Assert.DoesNotContain(Run(Snapshot(Production(classes: [ClassWithLevels("A", ("Level5", 100))]), wages: 20, expenses: 100)), f => f.Code == "production.budget-uniform");
        Assert.DoesNotContain(Run(Snapshot(Production(classes: [ClassWithLevels("A", ("Level5", 5))]), wages: 80)), f => f.Code == "production.budget-uniform");
    }
}
