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
