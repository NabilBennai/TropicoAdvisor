using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tropico.Analysis;
using Tropico.Localization;

namespace Tropico.Desktop.ViewModels;

public sealed record GoodRow(
    string Resource, string Price, string PriceChange, string Stock, string StockValue, string Exported, string Imported, string Routes);

public sealed class TradeViewModel
{
    public TradeViewModel(IslandSnapshot snapshot, IReadOnlyList<Finding> findings, ILocalizer? localizer = null)
    {
        var loc = localizer ?? Localizer.English;

        Goods = snapshot.Economy.Goods
            .Where(g => g.StockTotal > 0 || g.ExportedLast12Months > 0 || g.ImportedLast12Months > 0)
            .OrderByDescending(g => g.StockValue).ThenByDescending(g => g.StockTotal).ThenBy(g => g.Resource, System.StringComparer.Ordinal)
            .Select(g => ToRow(g, loc))
            .ToList();
        Findings = findings.Where(f => f.Category == "Trade").Select(f => new FindingViewModel(f, loc)).ToList();
    }

    public IReadOnlyList<GoodRow> Goods { get; }
    public IReadOnlyList<FindingViewModel> Findings { get; }
    public bool HasFindings => Findings.Count > 0;

    private static GoodRow ToRow(GoodSnapshot good, ILocalizer loc)
    {
        var culture = loc.Culture;
        return new GoodRow(
            loc.Term(Term.Resource(good.Resource)),
            good.CurrentPrice is { } price ? price.ToString("0.00", culture) : "-",
            good.PriceRatio is { } ratio ? (ratio - 1).ToString("+0.0%;-0.0%;0.0%", culture) : "-",
            N0(good.StockTotal, culture), N0(good.StockValue, culture), N0(good.ExportedLast12Months, culture), N0(good.ImportedLast12Months, culture),
            loc.Format("vm.routes", good.ExportOffers, good.ImportOffers));
    }

    private static string N0(double value, CultureInfo culture) => value.ToString("N0", culture);
}
