using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tropico.Desktop.Services;

namespace Tropico.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ISaveAnalysisService _service;
    private int _loadVersion;

    public MainViewModel() : this(new SaveAnalysisService())
    {
    }

    public MainViewModel(ISaveAnalysisService service)
    {
        _service = service;
    }

    public ObservableCollection<SaveFileItem> Saves { get; } = [];

    [ObservableProperty]
    public partial SaveFileItem? SelectedSave { get; set; }

    [ObservableProperty]
    public partial IslandReportViewModel? Report { get; private set; }

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    /// <summary>Lists the saves and analyses the most recent one.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        var previous = SelectedSave?.Path;

        Saves.Clear();
        foreach (var save in _service.ListSaves()) Saves.Add(save);

        StatusMessage = Saves.Count == 0 ? "No Tropico 6 save found." : null;

        var selected = Saves.Count == 0 ? null : Saves.FirstOrDefault(s => s.Path == previous) ?? Saves[0];
        if (SelectedSave == selected && selected is not null)
        {
            await LoadAsync(selected); // same selection: the change notification will not fire, reload explicitly
        }
        else
        {
            SelectedSave = selected; // triggers OnSelectedSaveChanged
        }
    }

    partial void OnSelectedSaveChanged(SaveFileItem? value)
    {
        if (value is null)
        {
            Report = null;
            return;
        }

        _ = LoadAsync(value);
    }

    private async Task LoadAsync(SaveFileItem save)
    {
        var version = ++_loadVersion;
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var report = await _service.AnalyzeAsync(save.Path);
            if (version != _loadVersion) return; // a newer selection superseded this one

            Report = new IslandReportViewModel(report);
        }
        catch (Exception e)
        {
            // UI boundary: any failure to read or decode a save is reported to the user instead of crashing the app.
            if (version != _loadVersion) return;

            Report = null;
            ErrorMessage = $"Could not read '{save.Name}': {e.Message}";
        }
        finally
        {
            if (version == _loadVersion) IsBusy = false;
        }
    }
}
