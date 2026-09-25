namespace Tropico.SaveParser;

/// <summary>Typed accessors over <see cref="T6Value"/>; each returns null when the value has another shape.</summary>
public static class T6ValueExtensions
{
    public static long? AsInteger(this T6Value? value) => value switch
    {
        T6IntegerValue i => i.Value,
        T6FloatValue f => (long)f.Value,
        _ => null,
    };

    public static double? AsDouble(this T6Value? value) => value switch
    {
        T6FloatValue f => f.Value,
        T6IntegerValue i => i.Value,
        _ => null,
    };

    public static string? AsName(this T6Value? value) => value switch
    {
        T6NameValue n => n.Value,
        T6StringValue s => s.Value,
        _ => null,
    };

    public static int? AsObjectIndex(this T6Value? value) => (value as T6ObjectRefValue)?.ObjectIndex;

    public static IReadOnlyList<T6Value> AsItems(this T6Value? value) => (value as T6ArrayValue)?.Items ?? [];
}
