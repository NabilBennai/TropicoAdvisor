using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tropico.Analysis;
using Tropico.SaveParser;

namespace Tropico.Desktop.Services;

public sealed record SaveFileItem(string Path, string Name, DateTime LastWriteTime, long SizeBytes)
{
    public string DisplayName => $"{Name}  ({LastWriteTime:g}, {SizeBytes / 1024.0 / 1024.0:0.0} MB)";
}

public interface ISaveAnalysisService
{
    IReadOnlyList<SaveFileItem> ListSaves();

    Task<IslandReport> AnalyzeAsync(string path, CancellationToken cancellationToken = default);
}

public sealed class SaveAnalysisService(string? saveDirectory = null) : ISaveAnalysisService
{
    private const string ProfileFileName = "Trop6_Profile.t6sav";
    private const string SavePrefix = "Trop6_Sav_";

    private readonly string _saveDirectory = saveDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Tropico6", "Saved", "SaveGames");

    public IReadOnlyList<SaveFileItem> ListSaves()
    {
        if (!Directory.Exists(_saveDirectory)) return [];

        return new DirectoryInfo(_saveDirectory)
            .EnumerateFiles("*.t6sav")
            .Where(f => !f.Name.Equals(ProfileFileName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => new SaveFileItem(f.FullName, DisplayName(f), f.LastWriteTime, f.Length))
            .ToList();
    }

    // The file is only ever read, never written.
    public Task<IslandReport> AnalyzeAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => new IslandAnalyzer().Analyze(T6SaveReader.Read(path)), cancellationToken);

    private static string DisplayName(FileInfo file)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(file.Name);
        return name.StartsWith(SavePrefix, StringComparison.Ordinal) ? name[SavePrefix.Length..] : name;
    }
}
