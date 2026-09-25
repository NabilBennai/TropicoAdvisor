using System.Collections.Generic;

namespace Tropico.SaveParser;

/// <summary>
/// Counts over the <c>T6Agent</c> objects. Agent blobs are only partially decoded (properties before the native segment),
/// and default values are not serialized, so a missing enum is counted under <see cref="DefaultValue"/>.
/// Agent objects include dead agents, so the totals exceed the living citizen counters.
/// </summary>
public sealed record T6AgentCensus(
    int AgentObjects,
    IReadOnlyDictionary<string, int> LifeState,
    IReadOnlyDictionary<string, int> Education,
    IReadOnlyDictionary<string, int> Ethnicity,
    IReadOnlyDictionary<string, int> HappinessLevel)
{
    public const string DefaultValue = "(default)";

    /// <summary>Agents that are not dead.</summary>
    public int LivingAgents { get; init; }

    /// <summary>Number of living agents currently having each thought (an agent counts once per thought), e.g. <c>ET6Thought::GoingToFindHome</c>.</summary>
    public IReadOnlyDictionary<string, int> ThoughtCounts { get; init; } = new Dictionary<string, int>();
}

public static class T6AgentCensusReader
{
    private const string DeadState = "ET6AgentLifeState::Dead";

    public static T6AgentCensus Read(T6SaveFile save)
    {
        var agents = save.ObjectTable.Objects.Where(o => o.Path == "/Script/Tropico6.T6Agent").Select(save.ReadObject).ToList();

        Dictionary<string, int> Count(string property) => agents
            .GroupBy(a => a.Find(property)?.Value.AsName() ?? T6AgentCensus.DefaultValue)
            .ToDictionary(g => g.Key, g => g.Count());

        var living = agents.Where(a => a.Find("LifeState")?.Value.AsName() != DeadState).ToList();
        var thoughts = living
            .SelectMany(a => a.Find("AgentThoughts")?.Value.AsItems().Select(t => t.AsName()).OfType<string>().Distinct() ?? [])
            .GroupBy(t => t)
            .ToDictionary(g => g.Key, g => g.Count());

        return new T6AgentCensus(agents.Count, Count("LifeState"), Count("education"), Count("Ethnicity"), Count("HappinessLevel"))
        {
            LivingAgents = living.Count,
            ThoughtCounts = thoughts,
        };
    }
}
