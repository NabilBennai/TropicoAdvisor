using System;
using System.IO;
using System.Threading;

namespace Tropico.Desktop.Services;

/// <summary>Signals that the game wrote a save, so the newest one can be analysed without pressing Refresh.</summary>
public interface ISaveWatcher : IDisposable
{
    event Action? SaveWritten;
}

/// <summary>Watches a save folder. The game writes a save in several steps, so events are merged until the folder has been quiet for a moment.</summary>
public sealed class FileSaveWatcher : ISaveWatcher
{
    public static readonly TimeSpan DefaultQuietPeriod = TimeSpan.FromSeconds(2);

    private readonly FileSystemWatcher? _watcher;
    private readonly Timer _timer;
    private readonly TimeSpan _quietPeriod;

    public FileSaveWatcher(string directory, TimeSpan? quietPeriod = null)
    {
        _quietPeriod = quietPeriod ?? DefaultQuietPeriod;
        _timer = new Timer(_ => SaveWritten?.Invoke());

        if (!Directory.Exists(directory)) return;

        _watcher = new FileSystemWatcher(directory, "*.t6sav")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        _watcher.Changed += (_, e) => Touched(e.Name);
        _watcher.Created += (_, e) => Touched(e.Name);
        _watcher.Renamed += (_, e) => Touched(e.Name);
        _watcher.EnableRaisingEvents = true;
    }

    public event Action? SaveWritten;

    private void Touched(string? name)
    {
        if (name is not null && name.Equals("Trop6_Profile.t6sav", StringComparison.OrdinalIgnoreCase)) return; // the profile changes constantly
        _timer.Change(_quietPeriod, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _timer.Dispose();
    }
}
