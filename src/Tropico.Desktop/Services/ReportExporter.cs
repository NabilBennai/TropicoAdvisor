using System.Globalization;
using System.Linq;
using System.Text;
using Tropico.Analysis;
using Tropico.Localization;

namespace Tropico.Desktop.Services;

/// <summary>Renders an analysis as a Markdown document in the chosen language.</summary>
public static class ReportExporter
{
    public static string ToMarkdown(IslandReport report, ILocalizer localizer)
    {
        var snapshot = report.Snapshot;
        var culture = localizer.Culture;
        var text = new StringBuilder();

        text.AppendLine($"# {snapshot.SaveName ?? localizer.Get("vm.unnamedSave")}").AppendLine();
        text.AppendLine($"## {localizer.Get("export.keyFigures")}").AppendLine();
        text.AppendLine($"- {localizer.Get("ui.buildings")}: {snapshot.Buildings.Total.ToString("N0", culture)}");
        if (snapshot.Population.Total is { } citizens) text.AppendLine($"- {localizer.Get("ui.citizens")}: {citizens.ToString("N0", culture)}");
        if (snapshot.Treasury is { } treasury) text.AppendLine($"- {localizer.Get("ui.treasury")}: {treasury.ToString("N0", culture)}");
        text.AppendLine();

        text.AppendLine($"## {localizer.Get("ui.suggestions")}").AppendLine();
        if (report.Findings.Count == 0) text.AppendLine(localizer.Get("export.noSuggestions"));

        foreach (var finding in report.Findings)
        {
            var label = localizer.Format("vm.findingLabel", new Term("severity", finding.Severity.ToString()), new Term("confidence", finding.Confidence.ToString()));
            text.AppendLine($"### {finding.MessageIn(localizer)}").AppendLine();
            text.AppendLine($"*{label} · {finding.Category}*").AppendLine();
            if (finding.SuggestionIn(localizer) is { Length: > 0 } action) text.AppendLine($"**{localizer.Get("export.action")}:** {action}").AppendLine();

            var evidence = finding.EvidenceIn(localizer);
            if (evidence.Count == 0) continue;
            text.AppendLine($"**{localizer.Get("export.evidence")}:**");
            foreach (var line in evidence) text.AppendLine($"- {line}");
            text.AppendLine();
        }

        return text.ToString();
    }
}
