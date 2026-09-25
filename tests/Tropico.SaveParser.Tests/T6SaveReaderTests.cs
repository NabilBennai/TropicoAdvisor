using System.Text;

namespace Tropico.SaveParser.Tests;

public class T6SaveReaderTests
{
    private const string SampleName = "Trop6_Sav_urss Oct, 1934.t6sav";

    internal static string FindSample(string sampleName = SampleName)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "private", sampleName);
            if (File.Exists(candidate)) return candidate;
        }

        var documents = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games", "Tropico6", "Saved", "SaveGames", sampleName);
        Assert.True(File.Exists(documents), $"Sample save not found: put '{sampleName}' in samples/private/.");
        return documents;
    }

    [Trait("Category", "RealSave")]
    [Fact]
    public void Read_ReferenceSave_ParsesContainer()
    {
        var path = FindSample();
        Assert.Equal("Lama", Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4));

        var save = T6SaveReader.Read(path);

        Assert.Equal("t6-#1290-win64-steam@dcffff2", save.Header.Build);
        Assert.Equal("urss Oct, 1934", save.Header.SaveName);
        Assert.Equal("#RMG#RMG_Map#4M69I4GGODHF0", save.Header.MapId);
        Assert.Equal("Steam_76561198137141740", save.Header.SteamId);
        Assert.Equal(0xDB, save.Header.CompressedDataOffset);
        Assert.Equal(10_431_425, save.DecompressedData.Length);
    }

    [Trait("Category", "RealSave")]
    [Fact]
    public void Read_ReferenceSave_ParsesNameTable()
    {
        var table = T6SaveReader.Read(FindSample()).NameTable;

        Assert.Equal(1192, table.Names.Count);
        Assert.Equal(0x6B5C, table.EndOffset);
        Assert.Equal("None", table.Names[0]);
        Assert.Equal("AgentList", table.Names[1]);
        Assert.Equal("ArrayProperty", table.Names[2]);
        Assert.Equal("ObjectProperty", table.Names[3]);
        Assert.Contains("IntProperty", table.Names);
        Assert.Contains("StructProperty", table.Names);
    }

    [Trait("Category", "RealSave")]
    [Fact]
    public void Read_ReferenceSave_ParsesObjectTable()
    {
        var table = T6SaveReader.Read(FindSample()).ObjectTable;

        // whole table consumed: 11,023 records, blobs start right after
        Assert.Equal(11_023, table.Objects.Count);
        Assert.Equal(0x9FDAF, table.BlobBaseOffset);
        Assert.All(table.Objects.Select((o, i) => (o, i)), x => Assert.Equal(x.i, x.o.Index));

        // first record: agent controller blueprint, blob at offset 0, flag 0
        var controller = table.Objects[0];
        Assert.Equal(2, controller.Kind);
        Assert.EndsWith("BP_T6AgentController_C", controller.Path);
        Assert.True(controller.HasBlob);
        Assert.Equal(0u, controller.BlobOffset);
        Assert.Null(controller.OwnerIndex);

        // agent with blob and 9-byte tail
        var agent = table.Objects[1];
        Assert.Equal("/Script/Tropico6.T6Agent", agent.Path);
        Assert.Equal((byte)1, agent.Flag);
        Assert.Equal(5954u, agent.BlobOffset);
        Assert.Equal(0, agent.OwnerIndex);

        // placed building actor: flag 2, 5-byte tail (no owner)
        var landmark = table.Objects[1451];
        Assert.EndsWith("BP_T6RegistanofSamarkand_C", landmark.Path);
        Assert.Equal((byte)2, landmark.Flag);
        Assert.True(landmark.HasBlob);
        Assert.Null(landmark.OwnerIndex);

        // kind 3: class reference, no blob at all
        var classRef = table.Objects[1452];
        Assert.Equal(3, classRef.Kind);
        Assert.StartsWith("#/Game/", classRef.Path);
        Assert.False(classRef.HasBlob);
        Assert.Null(classRef.Flag);
        Assert.Equal(483, table.Objects.Count(o => o.Kind == 3));

        // component owned by a building
        var site = table.Objects[7931];
        Assert.Equal("/Script/Tropico6.T6ConstructionSiteComponent", site.Path);
        Assert.Equal(1800, site.OwnerIndex);
        Assert.EndsWith("_C", table.Objects[1800].Path);
    }

    [Fact]
    public void NameTable_Truncated_Throws()
    {
        var data = new byte[] { 2, 0, 0, 0, 5, 0, 0, 0, (byte)'N', (byte)'o', (byte)'n', (byte)'e', 0 };
        Assert.Throws<InvalidDataException>(() => T6NameTable.Parse(data));
    }

    [Fact]
    public void Read_BadSignature_Throws()
    {
        var bytes = new byte[128];
        Encoding.ASCII.GetBytes("Nope").CopyTo(bytes, 0);
        Assert.Throws<InvalidDataException>(() => T6SaveReader.Read(bytes));
    }
}
