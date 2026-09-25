using System.Text.Json;

namespace Tropico.Localization.Tests;

public class LocalizedTextJsonTests
{
    [Fact]
    public void RoundTrip_KeepsKeyNumbersStringsTermsListsAndNestedTexts()
    {
        var original = LocalizedText.Of("finding.idleStock",
            30_000.5, Term.Resource("Coal"), 42, "raw text", true, null,
            TextList.Of([Term.Faction("Capitalists"), "Mexico"]),
            LocalizedText.Of("evidence.vacantHomes"),
            new Term("education", "High school", Lower: true));

        var restored = LocalizedTextJson.Deserialize(LocalizedTextJson.Serialize(original));

        Assert.Equal(original.Key, restored.Key);
        Assert.Equal(original.Args.Count, restored.Args.Count);
        Assert.Equal(30_000.5, restored.Args[0]);
        Assert.Equal(Term.Resource("Coal"), restored.Args[1]);
        Assert.Equal(42L, restored.Args[2]);     // integers come back as long: formatting is identical
        Assert.Equal("raw text", restored.Args[3]);
        Assert.Equal(true, restored.Args[4]);
        Assert.Null(restored.Args[5]);
        Assert.Equal(new Term("education", "High school", true), restored.Args[8]);
        Assert.Equal("Capitalists, Mexico", string.Join(", ", ((TextList)restored.Args[6]!).Items.Select(i => i is Term t ? t.Name : i)));
        Assert.Equal("evidence.vacantHomes", ((LocalizedText)restored.Args[7]!).Key);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("ar")]
    public void ARestoredText_RendersLikeTheOriginal_InEveryLanguage(string code)
    {
        var loc = Localizer.For(Languages.Find(code)!);
        var original = LocalizedText.Of("finding.exportConcentration", Term.Resource("Gold"), 80.0);

        var restored = LocalizedTextJson.Deserialize(LocalizedTextJson.Serialize(original));

        Assert.Equal(original.Render(loc), restored.Render(loc));
    }

    [Fact]
    public void RawTexts_SurviveTheRoundTrip()
    {
        var restored = LocalizedTextJson.Deserialize(LocalizedTextJson.Serialize(LocalizedText.Raw("BP_T6Mine_C")));

        Assert.True(restored.IsRaw);
        Assert.Equal("BP_T6Mine_C", restored.RawText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"k\":\"x\",\"a\":[{\"t\":\"mystery\"}]}")]
    public void InvalidJson_ThrowsAJsonException(string json) =>
        Assert.ThrowsAny<JsonException>(() => LocalizedTextJson.Deserialize(json));
}
