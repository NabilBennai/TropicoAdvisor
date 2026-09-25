using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tropico.Localization;

/// <summary>
/// JSON form of a <see cref="LocalizedText"/>, so a message can be stored and rendered later in any language.
/// Numbers and strings are plain JSON values; terms, lists and nested texts are small tagged objects.
/// </summary>
public static class LocalizedTextJson
{
    public static string Serialize(LocalizedText text) => ToNode(text).ToJsonString();

    public static LocalizedText Deserialize(string json) =>
        FromNode(JsonNode.Parse(json) ?? throw new JsonException("Empty localized text.")) as LocalizedText
        ?? throw new JsonException("Not a localized text.");

    private static JsonNode ToNode(LocalizedText text) => new JsonObject
    {
        ["k"] = text.Key,
        ["a"] = new JsonArray(text.Args.Select(ArgToNode).ToArray()),
    };

    private static JsonNode? ArgToNode(object? argument) => argument switch
    {
        null => null,
        LocalizedText text => new JsonObject { ["t"] = "text", ["v"] = ToNode(text) },
        Term term => new JsonObject { ["t"] = "term", ["kind"] = term.Kind, ["name"] = term.Name, ["lower"] = term.Lower },
        TextList list => new JsonObject { ["t"] = "list", ["items"] = new JsonArray(list.Items.Select(ArgToNode).ToArray()) },
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int or long or short or byte => JsonValue.Create(Convert.ToInt64(argument)),
        float or double or decimal => JsonValue.Create(Convert.ToDouble(argument)),
        _ => JsonValue.Create(Convert.ToString(argument, System.Globalization.CultureInfo.InvariantCulture)),
    };

    private static object? FromNode(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject obj when obj.ContainsKey("k"):
                return new LocalizedText(
                    obj["k"]!.GetValue<string>(),
                    (obj["a"] as JsonArray ?? []).Select(FromNode).ToList());
            case JsonObject obj:
                return obj["t"]?.GetValue<string>() switch
                {
                    "text" => FromNode(obj["v"]),
                    "term" => new Term(obj["kind"]!.GetValue<string>(), obj["name"]!.GetValue<string>(), obj["lower"]?.GetValue<bool>() ?? false),
                    "list" => new TextList((obj["items"] as JsonArray ?? []).Select(FromNode).ToList()),
                    var other => throw new JsonException($"Unknown localized argument '{other}'."),
                };
            case JsonValue value:
                if (value.TryGetValue<string>(out var s)) return s;
                if (value.TryGetValue<bool>(out var b)) return b;
                if (value.TryGetValue<long>(out var l)) return l;
                return value.GetValue<double>();
            default:
                throw new JsonException("Unsupported JSON node.");
        }
    }
}
