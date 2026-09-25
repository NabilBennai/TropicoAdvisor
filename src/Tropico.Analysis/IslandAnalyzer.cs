using Tropico.SaveParser;

namespace Tropico.Analysis;

public interface IAnalysisRule
{
    IEnumerable<Finding> Evaluate(IslandSnapshot snapshot);
}

public sealed class IslandAnalyzer(IEnumerable<IAnalysisRule> rules)
{
    public IslandAnalyzer() : this(DefaultRules)
    {
    }

    public static IReadOnlyList<IAnalysisRule> DefaultRules { get; } =
    [
        new BuildingConditionRule(),
        new TreasuryTrendRule(),
        new MonthlyBalanceRule(),
        new UnemploymentRule(),
        new HomelessFamiliesRule(),
        new HousingTierMismatchRule(),
        new HappinessRule(),
        new PopulationTrendRule(),
        new TreasuryRunwayRule(),
        new WageBurdenRule(),
        new CostCenterRule(),
        new ImportDependenceRule(),
        new ExportConcentrationRule(),
        new IdleStockRule(),
        new PriceOpportunityRule(),
    ];

    public IslandReport Analyze(IslandSnapshot snapshot) => new(
        snapshot,
        rules.SelectMany(r => r.Evaluate(snapshot)).OrderByDescending(f => f.Severity).ThenByDescending(f => f.Confidence).ToList());

    public IslandReport Analyze(T6SaveFile save) => Analyze(IslandSnapshotBuilder.Build(save));
}
