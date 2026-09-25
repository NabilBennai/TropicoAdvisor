using System.Text.RegularExpressions;

namespace Tropico.SaveParser;

/// <summary>
/// A resource deposit placed on the map (coal, iron, gold, oil, fish...). In the reference saves every deposit still holds its initial
/// amount even where mines have been running for years, so <see cref="Remaining"/> does not measure depletion.
/// <see cref="Radius"/> is in an unverified unit (values 2-4 against map distances of thousands).
/// </summary>
public sealed record T6Deposit(int ObjectIndex, string Resource, double X, double Y, double? Radius, double? Remaining);

public static partial class T6DepositReader
{
    [GeneratedRegex(@"^BP_T6(?<resource>[A-Za-z]+?)Deposit")]
    private static partial Regex DepositClass();

    public static IReadOnlyList<T6Deposit> Read(T6SaveFile save)
    {
        var deposits = new List<T6Deposit>();
        foreach (var record in save.ObjectTable.Objects)
        {
            if (record.Kind == 3 || !record.Path.Contains("/ResourceDeposits/", StringComparison.Ordinal)) continue;
            if (DepositClass().Match(record.ShortName) is not { Success: true } match) continue;

            var data = save.ReadObject(record);
            if (data.Transform is not { } transform) continue;

            deposits.Add(new T6Deposit(
                record.Index, match.Groups["resource"].Value, transform.X, transform.Y,
                data.Find("Radius")?.Value.AsDouble(), data.Find("RemainingResourceAmount")?.Value.AsDouble()));
        }

        return deposits;
    }
}
