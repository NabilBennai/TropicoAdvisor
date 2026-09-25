using Tropico.SaveParser;

Console.WriteLine("Tropico Advisor CLI");

Console.WriteLine($"Documents: {Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}");

var saveDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
    "My Games",
    "Tropico6",
    "Saved",
    "SaveGames"
);

Console.WriteLine($"Save directory: {saveDirectory}");
Console.WriteLine($"Exists: {Directory.Exists(saveDirectory)}");

if (!Directory.Exists(saveDirectory))
{
    Console.WriteLine("Save directory not found.");
    return;
}

var saveFiles = Directory.GetFiles(saveDirectory, "*.t6sav");

Console.WriteLine();
Console.WriteLine($"Found {saveFiles.Length} save(s):");

foreach (var saveFile in saveFiles)
{
    var info = new FileInfo(saveFile);

    Console.WriteLine(
        $"- {info.Name} | {info.Length:N0} bytes | {info.LastWriteTime}"
    );
}

var gameSave = saveFiles
    .Where(path => !Path.GetFileName(path)
        .Equals("Trop6_Profile.t6sav", StringComparison.OrdinalIgnoreCase))
    .OrderByDescending(File.GetLastWriteTime)
    .FirstOrDefault();

if (gameSave is null)
{
    Console.WriteLine("No game save found.");
    return;
}

Console.WriteLine();
Console.WriteLine($"Inspecting: {Path.GetFileName(gameSave)}");

var save = T6SaveReader.Read(gameSave);

Console.WriteLine($"Build: {save.Header.Build}");
Console.WriteLine($"Save: {save.Header.SaveName}");
Console.WriteLine($"Map: {save.Header.MapId}");
Console.WriteLine($"Compressed offset: 0x{save.Header.CompressedDataOffset:X}");
Console.WriteLine($"Decompressed size: {save.DecompressedData.Length}");

Console.WriteLine($"Names: {save.NameTable.Names.Count}");
Console.WriteLine($"Name table end: 0x{save.NameTable.EndOffset:X}");
Console.WriteLine("First names:");
foreach (var name in save.NameTable.Names.Take(10))
{
    Console.WriteLine($"- {name}");
}

Console.WriteLine($"Objects: {save.ObjectTable.Objects.Count}");
Console.WriteLine($"Blob base: 0x{save.ObjectTable.BlobBaseOffset:X}");

var buildings = T6BuildingReader.Read(save);
var stats = T6IslandStatisticsReader.Read(save);

Console.WriteLine();
Console.WriteLine($"Buildings: {buildings.Count}");
foreach (var group in buildings.GroupBy(b => b.ClassName).OrderByDescending(g => g.Count()).Take(5))
{
    Console.WriteLine($"- {group.Key}: {group.Count()}");
}

Console.WriteLine($"Citizens: {stats.Population.Total} (adults {stats.Population.Adults}, children {stats.Population.Children})");
Console.WriteLine($"Treasury: {stats.Treasury:N0}");
