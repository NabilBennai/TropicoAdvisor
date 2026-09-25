namespace Tropico.SaveParser.Tests;

public class T6PropertyReaderTests
{
    private static readonly Lazy<T6SaveFile> Urss = new(() => T6SaveReader.Read(T6SaveReaderTests.FindSample()));

    private static T6SaveFile Save => Urss.Value;

    [Fact]
    public void ReadObject_AllObjects_MatchReferenceStatusCounts()
    {
        var counts = Save.ObjectTable.Objects
            .Select(o => Save.ReadObject(o).Status)
            .GroupBy(s => s)
            .ToDictionary(g => g.Key, g => g.Count());

        // reference values from the Python decoder on the same save
        Assert.Equal(483, counts.GetValueOrDefault(T6ObjectDataStatus.NoBlob));
        Assert.Equal(8683, counts.GetValueOrDefault(T6ObjectDataStatus.Complete));
        Assert.Equal(465, counts.GetValueOrDefault(T6ObjectDataStatus.Resynchronized));
        Assert.Equal(1392, counts.GetValueOrDefault(T6ObjectDataStatus.Partial));
        Assert.Equal(0, counts.GetValueOrDefault(T6ObjectDataStatus.Failed));
    }

    [Fact]
    public void ReadObject_ClassReference_HasNoBlob()
    {
        var data = Save.ReadObject(Save.ObjectTable.Objects[1452]);

        Assert.Equal(T6ObjectDataStatus.NoBlob, data.Status);
        Assert.Empty(data.Properties);
    }

    [Fact]
    public void ReadObject_Agent_ReadsScalarEnumObjectAndFixedArrayProperties()
    {
        var agent = Save.ReadObject(Save.ObjectTable.Objects[1]);

        Assert.Equal(T6ObjectDataStatus.Partial, agent.Status); // native segment after the tagged properties
        Assert.Equal(new T6ObjectRefValue(1448), agent.Find("PlayerOwner")!.Value);
        Assert.Equal(new T6ObjectRefValue(1326), agent.Find("CurrentContainer")!.Value);
        Assert.Equal(new T6IntegerValue(12), agent.Find("FirstNameIndex")!.Value);
        Assert.Equal(new T6IntegerValue(14), agent.Find("Age")!.Value);
        Assert.Equal(new T6NameValue("ET6AgentLifeState::Adult"), agent.Find("LifeState")!.Value);
        Assert.Equal(new T6NameValue("ET6AgentEducation::HighSchool"), agent.Find("education")!.Value);

        // C-array elements share a name and differ by ArrayIndex
        Assert.Equal(new T6IntegerValue(636), agent.Find("ServerCurrentNeedStatus", 0)!.Value);
        Assert.Equal(new T6IntegerValue(624), agent.Find("ServerCurrentNeedStatus", 1)!.Value);
        Assert.Equal(6, agent.Properties.Count(p => p.Name == "ServerCurrentNeedStatus"));
        Assert.Equal(59.000004, ((T6FloatValue)agent.Find("CurrentHappiness")!.Value).Value, 5);

        var thoughts = Assert.IsType<T6ArrayValue>(agent.Find("AgentThoughts")!.Value);
        Assert.Contains(new T6NameValue("ET6Thought::Idle"), thoughts.Items);
    }

    [Fact]
    public void ReadObject_Building_ReadsTransformAndWorkmode()
    {
        var bunkhouse = Save.ReadObject(Save.ObjectTable.Objects[1458]);

        Assert.NotEqual(T6ObjectDataStatus.Failed, bunkhouse.Status);
        Assert.NotEqual(T6ObjectDataStatus.Partial, bunkhouse.Status);
        Assert.Equal(82500f, bunkhouse.Transform!.X);
        Assert.Equal(163500f, bunkhouse.Transform.Y);
        Assert.Equal(199.22021484375, bunkhouse.Transform.Z, 6);

        var workmode = Assert.IsType<T6ObjectRefValue>(bunkhouse.Find("CurrentWorkmode")!.Value);
        Assert.EndsWith("BP_T6WorkmodeNormalOccupancy_C", Save.ObjectTable.Objects[workmode.ObjectIndex].Path);

        var available = Assert.IsType<T6ArrayValue>(bunkhouse.Find("AvailableWorkmodes")!.Value);
        Assert.Equal([new T6ObjectRefValue(4691), new T6ObjectRefValue(4692)], available.Items);
    }

    [Fact]
    public void ReadObject_DataCollector_DecodesNestedStructsAndArrays()
    {
        var record = Save.ObjectTable.Objects.Single(o => o.Path.EndsWith("T6DataCollector", StringComparison.Ordinal));
        Assert.Equal(7791, record.Index);

        var collector = Save.ReadObject(record);

        Assert.Equal(new T6IntegerValue(1313), collector.Find("TotalCitizensNum")!.Value);
        Assert.Equal(new T6IntegerValue(952), collector.Find("AdultsNum")!.Value);

        var treasury = Assert.IsType<T6StructValue>(collector.Find("TreasuryHistory")!.Value);
        var entries = Assert.IsType<T6ArrayValue>(treasury.Find("Entries")!.Value);
        Assert.Equal(61, entries.Items.Count);

        var last = Assert.IsType<T6StructValue>(entries.Items[^1]);
        Assert.Equal(new T6FloatValue(417), last.Find("ValueX")!.Value);
        var y = Assert.IsType<T6ArrayValue>(last.Find("ValuesY")!.Value);
        Assert.Equal([new T6FloatValue(1453750.25)], y.Items);
    }
}
