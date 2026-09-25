using Tropico.Localization;
using Tropico.SaveParser;

namespace Tropico.Analysis.Tests;

public class LocalizedFindingsTests
{
    private static readonly Lazy<IReadOnlyList<Finding>> Findings =
        new(() => new IslandAnalyzer().Analyze(T6SaveReader.Read(IslandSnapshotBuilderTests.FindSample())).Findings);

    private static readonly string[] KeyPrefixes = ["finding.", "suggest.", "evidence.", "term.", "lead.", "concern.", "driver.", "fmt.", "list."];

    public static IEnumerable<object[]> AllLanguages() => Languages.All.Select(l => new object[] { l.Code });

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void EveryRealFinding_RendersInEveryLanguage_WithoutLeakingKeysOrPlaceholders(string code)
    {
        var loc = Localizer.For(Languages.Find(code)!);

        Assert.NotEmpty(Findings.Value);
        foreach (var finding in Findings.Value)
        {
            var texts = new[] { finding.MessageIn(loc), finding.SuggestionIn(loc) }.Concat(finding.EvidenceIn(loc)).OfType<string>().ToList();

            Assert.All(texts, t =>
            {
                Assert.False(string.IsNullOrWhiteSpace(t), $"{finding.Code}: empty text");
                Assert.DoesNotContain("{", t); // an unresolved placeholder
                Assert.DoesNotContain(KeyPrefixes, p => t.StartsWith(p, StringComparison.Ordinal)); // a catalog key shown instead of text
            });
        }
    }

    [Fact]
    public void TheSameFinding_ReadsDifferentlyInEachLanguage()
    {
        var homeless = Findings.Value.Single(f => f.Code == "housing.homeless-families");

        var rendered = Languages.All.ToDictionary(l => l.Code, l => homeless.MessageIn(Localizer.For(l)));

        Assert.Contains("48 families appear to be homeless", rendered["en"]);
        Assert.Contains("48 familles", rendered["fr"]);
        Assert.Contains("48 familias", rendered["es"]);
        Assert.Contains("48 famiglie", rendered["it"]);
        Assert.Matches(@"[؀-ۿ]", rendered["ar"]);
        Assert.Contains("127", rendered["ar"]); // numbers stay Latin in Arabic
        Assert.Equal(5, rendered.Values.Distinct().Count());
    }

    [Fact]
    public void EnglishRendering_IsUnchanged_ThroughTheCompatibilityProperties()
    {
        var wages = Findings.Value.Single(f => f.Code == "economy.wage-burden");

        Assert.Equal("Wages are 68 % of expenses.", wages.Message);
        Assert.Equal(wages.MessageIn(Localizer.English), wages.Message);
        Assert.Equal("Wages: 111,432", wages.Evidence[0]);
        Assert.NotNull(wages.Suggestion);
    }

    [Fact]
    public void GameTerms_InsideMessages_AreTranslated_WhileBuildingIdentifiersStayAsTheGameNamesThem()
    {
        var french = Localizer.For(Languages.French);
        var full = Findings.Value.Single(f => f.Code == "production.full-output.Coal");
        var faction = Findings.Value.Single(f => f.Code == "politics.faction-negative.Capitalists");

        Assert.Contains("producteur(s) de Charbon", full.MessageIn(french));
        Assert.Contains("BP_T6Mine_C", full.EvidenceIn(french)[0]);
        Assert.StartsWith("Capitalistes :", faction.MessageIn(french));
    }
}
