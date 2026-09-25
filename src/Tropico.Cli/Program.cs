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

using var stream = File.OpenRead(gameSave);

var header = new byte[Math.Min(64, stream.Length)];
stream.ReadExactly(header);

Console.WriteLine($"Size: {stream.Length:N0} bytes");
Console.WriteLine("First 64 bytes:");
Console.WriteLine(BitConverter.ToString(header).Replace("-", " "));

Console.WriteLine();
Console.WriteLine("ASCII strings:");

stream.Position = 0;

var data = new byte[stream.Length];
stream.ReadExactly(data);

var current = new List<char>();

foreach (var b in data)
{
    if (b >= 32 && b <= 126)
    {
        current.Add((char)b);
    }
    else
    {
        if (current.Count >= 6)
        {
            Console.WriteLine(new string(current.ToArray()));
        }

        current.Clear();
    }
}

if (current.Count >= 6)
{
    Console.WriteLine(new string(current.ToArray()));
}