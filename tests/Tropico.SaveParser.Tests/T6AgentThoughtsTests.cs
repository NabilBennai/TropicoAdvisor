namespace Tropico.SaveParser.Tests;

public class T6AgentThoughtsTests
{
    [Fact]
    public void AgentCensus_ReferenceSave_CountsLivingAgentsAndThoughts()
    {
        var census = T6AgentCensusReader.Read(T6SaveReader.Read(T6SaveReaderTests.FindSample()));

        Assert.Equal(1412, census.AgentObjects);
        Assert.Equal(1323, census.LivingAgents); // 1,412 agent objects minus 89 dead
        Assert.Equal(127, census.ThoughtCounts["ET6Thought::GoingToFindHome"]); // adults looking for a home
        Assert.True(census.ThoughtCounts["ET6Thought::GoingToFindJob"] > 0);
    }
}
