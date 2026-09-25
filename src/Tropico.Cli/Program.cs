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