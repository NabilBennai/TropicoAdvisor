using System.Text;

namespace Tropico.SaveParser.Tests;

public class T6SaveReaderTests
{
    private const string SampleName = "Trop6_Sav_urss Oct, 1934.t6sav";

    private static string FindSample()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "private", SampleName);
            if (File.Exists(candidate)) return candidate;
        }

        var documents = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games", "Tropico6", "Saved", "SaveGames", SampleName);
        Assert.True(File.Exists(documents), $"Sample save not found: put '{SampleName}' in samples/private/.");
        return documents;
    }

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

    [Fact]
    public void Read_BadSignature_Throws()
    {
        var bytes = new byte[128];
        Encoding.ASCII.GetBytes("Nope").CopyTo(bytes, 0);
        Assert.Throws<InvalidDataException>(() => T6SaveReader.Read(bytes));
    }
}
