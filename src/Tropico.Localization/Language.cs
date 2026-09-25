using System.Globalization;

namespace Tropico.Localization;

/// <summary>A supported UI language. <see cref="Culture"/> drives number and date formatting.</summary>
public sealed record Language(string Code, string NativeName, bool IsRightToLeft, CultureInfo Culture)
{
    public override string ToString() => NativeName;
}

public static class Languages
{
    // English uses the invariant culture on purpose: it keeps the historical English output ("1,453,750", "14 %") stable.
    public static Language English { get; } = new("en", "English", false, CultureInfo.InvariantCulture);

    public static Language French { get; } = new("fr", "Français", false, CultureInfo.GetCultureInfo("fr-FR"));

    public static Language Spanish { get; } = new("es", "Español", false, CultureInfo.GetCultureInfo("es-ES"));

    public static Language Italian { get; } = new("it", "Italiano", false, CultureInfo.GetCultureInfo("it-IT"));

    // Arabic keeps Latin digits and the invariant separators (1,453,750) so figures look the same in every language.
    public static Language Arabic { get; } = new("ar", "العربية", true, ArabicCulture());

    public static IReadOnlyList<Language> All { get; } = [English, French, Spanish, Italian, Arabic];

    public static Language Default => English;

    public static Language? Find(string? code) =>
        code is null ? null : All.FirstOrDefault(l => l.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The language matching a culture (by its two-letter code), or English when it is not supported.</summary>
    public static Language FromCulture(CultureInfo culture) => Find(culture.TwoLetterISOLanguageName) ?? Default;

    private static CultureInfo ArabicCulture()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("ar").Clone();
        culture.NumberFormat = (System.Globalization.NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        return culture;
    }
}
