using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Tropico.Localization;

public interface ILocalizer
{
    Language Language { get; }

    CultureInfo Culture { get; }

    /// <summary>The translation of a key; falls back to English, then to the key itself.</summary>
    string Get(string key);

    /// <summary>Translates a key and formats it with the language culture. Localizable arguments are rendered first.</summary>
    string Format(string key, params object?[] args);

    string Term(Term term);

    string Render(LocalizedText text);
}

public sealed class Localizer : ILocalizer
{
    private const string TermPrefix = "term.";
    private const string ListSeparatorKey = "list.separator";

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Catalogs = [];
    private static readonly Dictionary<string, Localizer> Instances = [];
    private static readonly object Gate = new();

    private readonly IReadOnlyDictionary<string, string> _catalog;
    private readonly IReadOnlyDictionary<string, string> _english;

    private Localizer(Language language)
    {
        Language = language;
        _catalog = LoadCatalog(language.Code);
        _english = language.Code == "en" ? _catalog : LoadCatalog("en");
    }

    /// <summary>English rendering (invariant culture): the reference used by tests and as a fallback.</summary>
    public static Localizer English => For(Languages.English);

    public static Localizer For(Language language)
    {
        lock (Gate)
        {
            if (!Instances.TryGetValue(language.Code, out var instance)) Instances[language.Code] = instance = new Localizer(language);
            return instance;
        }
    }

    public Language Language { get; }

    public CultureInfo Culture => Language.Culture;

    /// <summary>Every key of a language's catalog (used to check that translations are complete).</summary>
    public static IReadOnlyCollection<string> Keys(Language language) => LoadCatalog(language.Code).Keys.ToList();

    public string Get(string key) =>
        _catalog.TryGetValue(key, out var value) ? value : _english.TryGetValue(key, out var fallback) ? fallback : key;

    public string Format(string key, params object?[] args)
    {
        var template = Get(key);
        if (args.Length == 0) return template;

        return string.Format(Culture, template, args.Select(Resolve).ToArray());
    }

    public string Term(Term term)
    {
        var key = $"{TermPrefix}{term.Kind}.{term.Name}";
        var text = _catalog.TryGetValue(key, out var value) ? value : _english.TryGetValue(key, out var fallback) ? fallback : term.Name;
        return term.Lower ? text.ToLower(Culture) : text;
    }

    public string Render(LocalizedText text) => text.IsRaw ? text.RawText : Format(text.Key, text.Args.ToArray());

    private object? Resolve(object? argument) => argument switch
    {
        LocalizedText text => Render(text),
        Term term => Term(term),
        TextList list => string.Join(Get(ListSeparatorKey), list.Items.Select(Resolve)),
        _ => argument,
    };

    private static IReadOnlyDictionary<string, string> LoadCatalog(string code)
    {
        lock (Gate)
        {
            if (Catalogs.TryGetValue(code, out var cached)) return cached;

            var assembly = typeof(Localizer).Assembly;
            var resource = $"Tropico.Localization.Resources.{code}.json";
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing language resource '{resource}'.");
            var catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? throw new InvalidOperationException($"Language resource '{resource}' is empty.");

            return Catalogs[code] = catalog;
        }
    }
}
