using Tropico.Desktop.Services;

namespace Tropico.Desktop.Tests;

public class SaveAnalysisServiceTests : IDisposable
{
    private readonly string _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tropico-tests-" + Guid.NewGuid())).FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private void Create(string name, DateTime lastWrite)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        File.SetLastWriteTime(path, lastWrite);
    }

    [Fact]
    public void ListSaves_ExcludesProfile_AndOrdersNewestFirst()
    {
        Create("Trop6_Sav_old.t6sav", new DateTime(2026, 1, 1));
        Create("Trop6_Sav_new.t6sav", new DateTime(2026, 3, 1));
        Create("Trop6_Profile.t6sav", new DateTime(2026, 4, 1));
        Create("notes.txt", new DateTime(2026, 5, 1));

        var saves = new SaveAnalysisService(_directory).ListSaves();

        Assert.Equal(["new", "old"], saves.Select(s => s.Name));
    }

    [Fact]
    public void ListSaves_MissingDirectory_ReturnsEmpty()
    {
        Assert.Empty(new SaveAnalysisService(Path.Combine(_directory, "missing")).ListSaves());
    }

    [Fact]
    public async Task AnalyzeAsync_InvalidFile_Throws()
    {
        Create("Trop6_Sav_bad.t6sav", DateTime.Now);

        await Assert.ThrowsAsync<InvalidDataException>(() => new SaveAnalysisService(_directory).AnalyzeAsync(Path.Combine(_directory, "Trop6_Sav_bad.t6sav")));
    }
}
