namespace Tropico.SaveParser.Tests;

[Trait("Category", "RealSave")]
public class T6DepositTests
{
    private static readonly Lazy<IReadOnlyList<T6Deposit>> Deposits = new(() => T6DepositReader.Read(T6SaveReader.Read(T6SaveReaderTests.FindSample())));

    [Trait("Category", "RealSave")]
    [Fact]
    public void Read_ReferenceSave_FindsEveryDepositByResource()
    {
        var byResource = Deposits.Value.GroupBy(d => d.Resource).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(74, Deposits.Value.Count);
        Assert.Equal(
            new Dictionary<string, int> { ["Oil"] = 16, ["Fish"] = 10, ["Nickel"] = 8, ["Iron"] = 8, ["Aluminum"] = 8, ["Gold"] = 8, ["Coal"] = 8, ["Uranium"] = 8 },
            byResource);
    }

    [Trait("Category", "RealSave")]
    [Fact]
    public void Read_ReferenceSave_ExposesPositionRadiusAndAmount()
    {
        var fish = Deposits.Value.First(d => d.Resource == "Fish");

        Assert.Equal(-37500, fish.X);
        Assert.Equal(151554.4375, fish.Y, 3);
        Assert.Equal(3.28, fish.Radius!.Value, 2);
        Assert.Equal(200_000, fish.Remaining);

        // initial amounts per deposit type; none is depleted in this save even where mines run
        Assert.All(Deposits.Value.Where(d => d.Resource == "Coal"), d => Assert.Equal(800_000, d.Remaining));
        Assert.All(Deposits.Value.Where(d => d.Resource == "Gold"), d => Assert.Equal(240_000, d.Remaining));
        Assert.All(Deposits.Value.Where(d => d.Resource == "Uranium"), d => Assert.Equal(160_000, d.Remaining));
    }
}
