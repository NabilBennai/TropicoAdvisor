namespace Tropico.Analysis;

/// <summary>Shared game-month to calendar date conversion (history X values are month indices).</summary>
public static class GameCalendar
{
    public static (int Year, int Month)? DateOf(int? currentIndex, int? year, int? month, double monthIndex)
    {
        if (currentIndex is not { } current || year is not { } y || month is not { } m) return null;

        var total = y * 12 + (m - 1) - (current - (int)monthIndex);
        var (years, months) = Math.DivRem(total, 12);
        return (years, months + 1);
    }
}

/// <summary>
/// Population, housing, jobs and happiness details. Facts about each field:
/// <list type="bullet">
/// <item>Verified against agent data: education order (uneducated, high school, college), age groups (children, adults, retired),
/// overall happiness, happiness category positions, happiness level counts (low, medium).</item>
/// <item>Assumed: unemployed series use the same education order; homeless families and vacant homes use three housing tiers
/// (cheapest first); the 5 wealth classes are ordered lowest first.</item>
/// </list>
/// History arrays omit trailing zeros, so every list is padded to its fixed length.
/// </summary>
public sealed record PopulationDetails(
    IReadOnlyList<double> Education,
    IReadOnlyList<double> AgeGroups,
    IReadOnlyList<double> Wealth,
    IReadOnlyList<double> Unemployed,
    IReadOnlyList<double> HomelessFamilies,
    IReadOnlyList<double> VacantHomes,
    double? OpenJobs,
    double? HappinessOverall,
    IReadOnlyDictionary<string, double> HappinessByCategory,
    IReadOnlyDictionary<string, double> HappinessLevels,
    double? AverageAge,
    int? CitizensLookingForHome)
{
    public static readonly string[] EducationLevels = ["Uneducated", "High school", "College"];
    public static readonly string[] AgeGroupNames = ["Children", "Adults", "Retired"];
    public static readonly string[] HousingTiers = ["Tier 1 (cheapest)", "Tier 2", "Tier 3"];

    public int? MonthIndex { get; init; }
    public int? Year { get; init; }
    public int? Month { get; init; }

    public IReadOnlyList<TimePoint> PopulationHistory { get; init; } = [];
    public IReadOnlyList<TimePoint> UnemployedHistory { get; init; } = [];
    public IReadOnlyList<TimePoint> HomelessFamiliesHistory { get; init; } = [];
    public IReadOnlyList<TimePoint> VacantHomesHistory { get; init; } = [];
    public IReadOnlyList<TimePoint> HappinessHistory { get; init; } = [];

    public double TotalUnemployed => Unemployed.Sum();
    public double TotalHomelessFamilies => HomelessFamilies.Sum();
    public double TotalVacantHomes => VacantHomes.Sum();

    public (int Year, int Month)? DateOf(double monthIndex) => GameCalendar.DateOf(MonthIndex, Year, Month, monthIndex);

    public static PopulationDetails Empty { get; } =
        new([], [], [], [], [], [], null, null, new Dictionary<string, double>(), new Dictionary<string, double>(), null, null);
}
