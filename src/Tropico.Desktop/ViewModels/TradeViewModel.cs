using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tropico.Analysis;

namespace Tropico.Desktop.ViewModels;

public sealed record GoodRow(
    string Resource, string Price, string PriceChange, string Stock, string StockValue, string Exported, string Imported, string Routes);

public sealed class TradeViewModel
{
    public TradeViewModel(IslandSnapshot snapshot, IReadOnlyList<Finding> findings)
    {
        Goods = snapshot.Economy.Goods
            .Where(g => g.StockTotal > 0 || g.ExportedLast12Months > 0 || g.ImportedLast12Months > 0)
            .OrderByDescending(g => g.StockValue).ThenByDescending(g => g.StockTotal).ThenBy(g => g.Resource, System.StringComparer.Ordinal)
            .Select(ToRow)
            .ToList();
        Findings = findings.Where(f => f.Category == "Trade").Select(f => new FindingViewModel(f)).ToList();
    }

    public IReadOnlyList<GoodRow> Goods { get; }
    public IReadOnlyList<FindingViewModel> Findings { get; }
    public bool HasFindings => Findings.Count > 0;

    private static GoodRow ToRow(GoodSnapshot good) => new(
        DisplayNames.Category(good.Resource),
        good.CurrentPrice is { } price ? price.ToString("0.00", CultureInfo.CurrentCulture) : "-",
        good.PriceRatio is { } ratio ? $"{ratio - 1:+0.0%;-0.0%;0.0%}" : "-",
        N0(good.StockTotal), N0(good.StockValue), N0(good.ExportedLast12Months), N0(good.ImportedLast12Months),
        $"{good.ExportOffers} / {good.ImportOffers}");

    private static string N0(double value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
