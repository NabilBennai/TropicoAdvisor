using System.Globalization;
using System.Text.RegularExpressions;

namespace Tropico.Localization.Tests;

public partial class CatalogTests
{
    [GeneratedRegex(@"\{(\d+)")]
    private static partial Regex Placeholder();

    public static IEnumerable<object[]> OtherLanguages() => Languages.All.Where(l => l.Code != "en").Select(l => new object[] { l.Code });

    [Fact]
    public void FiveLanguagesAreSupported()
    {
        Assert.Equal(["en", "fr", "es", "it", "ar"], Languages.All.Select(l => l.Code));
        Assert.Equal(["English", "Français", "Español", "Italiano", "العربية"], Languages.All.Select(l => l.NativeName));
        Assert.Equal(["ar"], Languages.All.Where(l => l.IsRightToLeft).Select(l => l.Code));
    }

    [Theory]
    [MemberData(nameof(OtherLanguages))]
    public void EveryLanguage_HasExactlyTheKeysOfEnglish(string code)
    {
        var english = Localizer.Keys(Languages.English).ToHashSet();
        var other = Localizer.Keys(Languages.Find(code)!).ToHashSet();

        Assert.Empty(english.Except(other)); // untranslated keys
        Assert.Empty(other.Except(english)); // keys that no longer exist in English
    }

    [Theory]
    [MemberData(nameof(OtherLanguages))]
    public void EveryTranslation_KeepsTheSamePlaceholdersAsEnglish(string code)
    {
        var english = Localizer.For(Languages.English);
        var translated = Localizer.For(Languages.Find(code)!);

        foreach (var key in Localizer.Keys(Languages.English))
        {
            var expected = Placeholder().Matches(english.Get(key)).Select(m => m.Groups[1].Value).Distinct().Order().ToList();
            var actual = Placeholder().Matches(translated.Get(key)).Select(m => m.Groups[1].Value).Distinct().Order().ToList();
            Assert.True(expected.SequenceEqual(actual), $"{code}/{key}: placeholders {string.Join(",", actual)} != {string.Join(",", expected)}");
        }
    }

    [Theory]
    [MemberData(nameof(OtherLanguages))]
    public void NoTranslationIsEmpty_AndSentencesAreReallyTranslated(string code)
    {
        var english = Localizer.For(Languages.English);
        var translated = Localizer.For(Languages.Find(code)!);
        var identical = new List<string>();

        foreach (var key in Localizer.Keys(Languages.English))
        {
            Assert.False(string.IsNullOrWhiteSpace(translated.Get(key)), $"{code}/{key} is empty");

            // long texts must differ from English (short labels such as "Info" or "Budget" may legitimately be the same)
            if (english.Get(key).Length > 40 && translated.Get(key) == english.Get(key)) identical.Add(key);
        }

        Assert.Empty(identical);
    }

    [Fact]
    public void Arabic_UsesArabicScriptForSentences()
    {
        var arabic = Localizer.For(Languages.Arabic);

        foreach (var key in Localizer.Keys(Languages.English).Where(k => k.StartsWith("finding.") || k.StartsWith("suggest.") || k.StartsWith("ui.")))
        {
            Assert.Matches(@"[؀-ۿ]", arabic.Get(key));
        }
    }
}

public class LanguageTests
{
    [Theory]
    [InlineData("fr", "fr")]
    [InlineData("FR", "fr")]
    [InlineData(" es ", "es")]
    [InlineData("it", "it")]
    [InlineData("ar", "ar")]
    public void Find_MatchesCodesIgnoringCaseAndSpaces(string code, string expected) => Assert.Equal(expected, Languages.Find(code)!.Code);

    [Theory]
    [InlineData("de")]
    [InlineData("")]
    [InlineData(null)]
    public void Find_ReturnsNullForUnsupportedCodes(string? code) => Assert.Null(Languages.Find(code));

    [Theory]
    [InlineData("fr-CA", "fr")]
    [InlineData("es-MX", "es")]
    [InlineData("ar-SA", "ar")]
    [InlineData("en-GB", "en")]
    [InlineData("ja-JP", "en")] // unsupported: falls back to English
    public void FromCulture_UsesTheTwoLetterLanguage(string culture, string expected) =>
        Assert.Equal(expected, Languages.FromCulture(CultureInfo.GetCultureInfo(culture)).Code);
}

public class LocalizerTests
{
    private static string Digits(string text) => new(text.Where(char.IsDigit).ToArray());

    [Fact]
    public void English_KeepsTheHistoricalInvariantFormats()
    {
        var english = Localizer.English;

        Assert.Equal("Treasury: 1,453,750", english.Format("evidence.treasury", 1453750.25));
        Assert.Equal("Wages are 68 % of expenses.", english.Format("finding.wageBurden", 67.6));
        Assert.Equal("Price: 2.51", english.Format("evidence.price", 2.5123));
    }

    [Fact]
    public void Numbers_FollowTheLanguageCulture_ButKeepLatinDigits()
    {
        var french = Localizer.For(Languages.French).Format("evidence.treasury", 1453750.25);
        var spanish = Localizer.For(Languages.Spanish).Format("evidence.stock", 3964.0);
        var arabic = Localizer.For(Languages.Arabic).Format("evidence.treasury", 1453750.25);

        Assert.Equal("1453750", Digits(french));
        Assert.DoesNotContain("1,453,750", french);               // French groups with a space, not a comma
        Assert.Matches(@"^Trésor : 1\s1?453\s750$|^Trésor : 1[   ]453[   ]750$", french);
        Assert.Contains("3964", Digits(spanish));
        Assert.Equal("الخزينة: 1,453,750", arabic);               // Arabic keeps Latin digits and the same separators as English
    }

    [Fact]
    public void Terms_AreTranslated_LowerCasedOnRequest_AndFallBackToTheGameName()
    {
        var french = Localizer.For(Languages.French);

        Assert.Equal("Charbon", french.Term(Term.Resource("Coal")));
        Assert.Equal("Capitalistes", french.Term(Term.Faction("Capitalists")));
        Assert.Equal("lycée", french.Term(new Term("education", "High school", Lower: true)));
        Assert.Equal("SomethingNew", french.Term(Term.Resource("SomethingNew"))); // unknown vocabulary: the game's own name
    }

    [Fact]
    public void UnknownKeys_ReturnTheKey_SoAMissingTranslationIsVisibleNotFatal()
    {
        Assert.Equal("no.such.key", Localizer.English.Get("no.such.key"));
        Assert.Equal("no.such.key", Localizer.For(Languages.Italian).Get("no.such.key"));
    }

    [Fact]
    public void Lists_UseTheLanguageSeparator_AndNestedTextsAreRendered()
    {
        var names = TextList.Of([Term.Faction("Capitalists"), Term.Faction("Communists")]);

        Assert.Equal("Capitalists, Communists", Localizer.English.Format("fmt.countName", names, "").Trim().TrimEnd(','));
        Assert.Equal("الرأسماليون، الشيوعيون", Localizer.For(Languages.Arabic).Format("fmt.countName", names, "").Trim());

        var nested = LocalizedText.Of("fmt.countName", 3, LocalizedText.Of("evidence.vacantHomes"));
        Assert.Equal("3 Vacant homes", nested.Render(Localizer.English));
        Assert.Equal("3 Logements vacants", nested.Render(Localizer.For(Languages.French)));
    }

    [Fact]
    public void RawText_IsNeverTranslated()
    {
        var raw = LocalizedText.Raw("BP_T6Mine_C");

        Assert.True(raw.IsRaw);
        Assert.Equal("BP_T6Mine_C", raw.Render(Localizer.For(Languages.Arabic)));
        Assert.Equal("BP_T6Mine_C", raw.ToString());
    }
}
