namespace Tropico.Localization;

/// <summary>
/// A message identified by a catalog key plus its arguments. It is rendered late, in the language the user picked, so the same
/// analysis result can be shown in any language. Arguments may be numbers (formatted with the language culture), strings,
/// <see cref="Term"/>s (game vocabulary), <see cref="TextList"/>s or nested <see cref="LocalizedText"/>s.
/// </summary>
public sealed record LocalizedText(string Key, IReadOnlyList<object?> Args)
{
    private const string RawPrefix = "=";

    public static LocalizedText Of(string key, params object?[] args) => new(key, args);

    /// <summary>Text that is already final and is not translated (identifiers, names typed by the game).</summary>
    public static LocalizedText Raw(string text) => new(RawPrefix + text, []);

    public bool IsRaw => Key.StartsWith(RawPrefix, StringComparison.Ordinal);

    public string RawText => Key[RawPrefix.Length..];

    public string Render(ILocalizer localizer) => localizer.Render(this);

    public override string ToString() => Localizer.English.Render(this);
}

/// <summary>A word of the game vocabulary (a resource, a faction, an education level...) translated through the catalog.</summary>
/// <param name="Kind">Vocabulary group, e.g. "resource", "faction", "education".</param>
/// <param name="Name">The game's own name, e.g. "Coal". Used as is when the catalog has no translation.</param>
/// <param name="Lower">Render in lower case (for use inside a sentence).</param>
public sealed record Term(string Kind, string Name, bool Lower = false)
{
    public static Term Resource(string name) => new("resource", name);

    public static Term Faction(string name) => new("faction", name);
}

/// <summary>Items joined with the language's list separator (", " or "، ").</summary>
public sealed record TextList(IReadOnlyList<object?> Items)
{
    public static TextList Of(IEnumerable<object?> items) => new(items.ToList());
}
