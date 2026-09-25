using System.Text;

namespace Tropico.Desktop.ViewModels;

/// <summary>Readable labels for game identifiers.</summary>
public static class DisplayNames
{
    /// <summary>"BP_T6PileOfBS_C" -> "Pile Of BS", "Building_Palace_C" -> "Palace", "BP_Building_Teamster_C" -> "Teamster".</summary>
    public static string Building(string className)
    {
        var name = className;
        if (name.EndsWith("_C", System.StringComparison.Ordinal)) name = name[..^2];
        foreach (var prefix in new[] { "BP_", "Building_", "T6" })
        {
            if (name.StartsWith(prefix, System.StringComparison.Ordinal)) name = name[prefix.Length..];
        }

        return Humanize(name.Replace('_', ' '));
    }

    /// <summary>"TouristFees" -> "Tourist Fees"; "ET6ResourceType::Gold" -> "Gold".</summary>
    public static string Category(string name)
    {
        var separator = name.LastIndexOf("::", System.StringComparison.Ordinal);
        return Humanize(separator < 0 ? name : name[(separator + 2)..]);
    }

    // inserts a space before an upper-case letter that follows a lower-case letter or digit
    private static string Humanize(string value)
    {
        var text = new StringBuilder(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            if (i > 0 && char.IsUpper(value[i]) && (char.IsLower(value[i - 1]) || char.IsDigit(value[i - 1]))) text.Append(' ');
            text.Append(value[i]);
        }

        return text.ToString().Trim();
    }
}
