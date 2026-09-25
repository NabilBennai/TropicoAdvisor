using Tropico.Localization;

namespace Tropico.Analysis;

public enum Severity { Info, Warning, Critical }

/// <summary>How much the underlying save data can be trusted (see the reverse-engineering report).</summary>
public enum Confidence { Uncertain, Probable, Verified }

/// <summary>
/// One observation about the island. The message, the suggestion and the evidence are <see cref="LocalizedText"/>s: they are kept
/// as catalog keys plus arguments and rendered in the user's language. <see cref="Message"/>, <see cref="Suggestion"/> and
/// <see cref="Evidence"/> give the English rendering. <see cref="Suggestion"/> is a general lead, not a precise instruction;
/// the evidence lists the figures the finding is based on.
/// </summary>
public sealed record Finding
{
    public Finding(string code, Severity severity, Confidence confidence, LocalizedText message)
    {
        Code = code;
        Severity = severity;
        Confidence = confidence;
        MessageText = message;
    }

    /// <summary>A finding whose message is already final text (not translated).</summary>
    public Finding(string code, Severity severity, Confidence confidence, string message)
        : this(code, severity, confidence, LocalizedText.Raw(message))
    {
    }

    public string Code { get; }
    public Severity Severity { get; }
    public Confidence Confidence { get; }
    public LocalizedText MessageText { get; }

    public string Category { get; init; } = "General";
    public LocalizedText? SuggestionText { get; init; }
    public IReadOnlyList<LocalizedText> EvidenceTexts { get; init; } = [];

    public string Message => MessageIn(Localizer.English);
    public string? Suggestion => SuggestionIn(Localizer.English);
    public IReadOnlyList<string> Evidence => EvidenceIn(Localizer.English);

    public string MessageIn(ILocalizer localizer) => MessageText.Render(localizer);
    public string? SuggestionIn(ILocalizer localizer) => SuggestionText?.Render(localizer);
    public IReadOnlyList<string> EvidenceIn(ILocalizer localizer) => EvidenceTexts.Select(e => e.Render(localizer)).ToList();
}

public sealed record IslandReport(IslandSnapshot Snapshot, IReadOnlyList<Finding> Findings);

/// <summary>Shorthand to build localizable texts inside the rules.</summary>
internal static class L
{
    public static LocalizedText T(string key, params object?[] args) => LocalizedText.Of(key, args);
}
