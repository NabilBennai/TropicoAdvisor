using Tropico.Analysis;
using Tropico.Desktop.Services;
using Tropico.Localization;

namespace Tropico.Desktop.Tests;

public class ReportExporterTests
{
    private static IslandReport Report(params Finding[] findings) => new(
        new IslandSnapshot("My island", "t6", new BuildingSummary(1234, new Dictionary<string, int>(), new Dictionary<string, int>()),
            new PopulationSummary(1300, 200, 900, 0, 0), 12_345.6, 0, [], [], [], []),
        findings);

    private static Finding Sample() => new("wages", Severity.Warning, Confidence.Probable, LocalizedText.Of("finding.wageBurden", 62.0))
    {
        Category = "Economy",
        SuggestionText = LocalizedText.Of("suggest.wageBurden"),
        EvidenceTexts = [LocalizedText.Of("evidence.wages", 5000), LocalizedText.Of("evidence.upkeep", 300)],
    };

    [Fact]
    public void TheMarkdown_HasTitle_KeyFigures_AndEachSuggestionWithActionAndEvidence()
    {
        var markdown = ReportExporter.ToMarkdown(Report(Sample()), Localizer.English);

        Assert.StartsWith("# My island", markdown);
        Assert.Contains("- Buildings: 1,234", markdown);
        Assert.Contains("- Treasury: 12,346", markdown);
        Assert.Contains("### Wages are 62 % of expenses.", markdown);
        Assert.Contains("*Warning · Probable · Economy*", markdown);
        Assert.Contains("**Suggested action:** Check the wage level", markdown);
        Assert.Contains("- Wages: 5,000", markdown);
        Assert.Contains("- Upkeep: 300", markdown);
    }

    [Fact]
    public void WithoutFindings_SaysSo()
    {
        Assert.Contains("No suggestions for this save.", ReportExporter.ToMarkdown(Report(), Localizer.English));
    }

    [Fact]
    public void TheDocument_FollowsTheLanguage()
    {
        var french = ReportExporter.ToMarkdown(Report(Sample()), Localizer.For(Languages.French));

        Assert.Contains("## Chiffres clés", french);
        Assert.Contains("**Action suggérée:**", french);
        Assert.Contains("**Preuves:**", french);
        Assert.DoesNotContain("Suggested action", french);
    }
}

public class OverviewFilterTests
{
    private static Finding F(string code, Severity severity, Confidence confidence) =>
        new(code, severity, confidence, code) { Category = "Economy" };

    private static Desktop.ViewModels.IslandReportViewModel Overview() => new(new IslandReport(
        new IslandSnapshot("s", "t6", new BuildingSummary(1, new Dictionary<string, int>(), new Dictionary<string, int>()),
            new PopulationSummary(10, 2, 8, 0, 0), 1, 0, [], [], [], []),
        [F("info", Severity.Info, Confidence.Verified), F("warn", Severity.Warning, Confidence.Uncertain), F("crit", Severity.Critical, Confidence.Probable)]));

    [Fact]
    public void ByDefault_EverythingIsShown_WithACount()
    {
        var vm = Overview();

        Assert.Equal(["info", "warn", "crit"], vm.Findings.Select(f => f.Message));
        Assert.Equal("3 of 3 shown", vm.FilterSummary);
        Assert.True(vm.HasAnyFindings);
    }

    [Theory]
    [InlineData(1, new[] { "warn", "crit" })]
    [InlineData(2, new[] { "crit" })]
    public void TheSeverityFilter_KeepsTheSelectedLevelAndAbove(int filter, string[] expected)
    {
        var vm = Overview();

        vm.SelectedFilter = filter;

        Assert.Equal(expected, vm.Findings.Select(f => f.Message));
    }

    [Fact]
    public void HidingUncertain_DropsFindingsOnUnverifiedData_AndTheEmptyStateFollows()
    {
        var vm = Overview();
        vm.HideUncertain = true;
        Assert.Equal(["info", "crit"], vm.Findings.Select(f => f.Message));

        vm.SelectedFilter = 2;
        vm.HideUncertain = false;
        vm.SelectedFilter = 0;
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.SelectedFilter = 2;

        Assert.Contains("HasFindings", changed);
        Assert.Equal("1 of 3 shown", vm.FilterSummary);
    }
}
