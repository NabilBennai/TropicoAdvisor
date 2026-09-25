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