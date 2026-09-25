using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tropico.Analysis;
using Tropico.Desktop.Localization;
using Tropico.Desktop.Services;
using Tropico.Localization;

namespace Tropico.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ISaveAnalysisService _service;
    private readonly Translator _translator;
    private readonly ISettingsStore? _settings;
    private int _loadVersion;
    private IslandReport? _report;
    private bool _initialising = true;
    private string? _statusKey;
    private (string Save, string Detail)? _error;

    /// <summary>The application: real save folder, saved language choice, the shared translator used by the XAML labels.</summary>
    public MainViewModel() : this(new SaveAnalysisService(), Translator.Instance, new FileSettingsStore(), useSystemLanguage: true)
    {
    }

    public MainViewModel(ISaveAnalysisService service, Translator? translator = null, ISettingsStore? settings = null, bool useSystemLanguage = false)
    {
        _service = service;
        _translator = translator ?? new Translator();
        _settings = settings;

        if (useSystemLanguage)
        {
            _translator.SetLanguage(LanguageResolver.Resolve(settings, CultureInfo.CurrentUICulture, Environment.GetEnvironmentVariable(LanguageResolver.EnvironmentVariable)));
        }

        SelectedLanguage = Languages.All.First(l => l.Code == _translator.Language.Code);
        _initialising = false;
    }

    public ObservableCollection<SaveFileItem> Saves { get; } = [];

    public IReadOnlyList<Language> AvailableLanguages => Languages.All;

    /// <summary>Right to left for Arabic; the whole window mirrors (charts keep their own left-to-right time axis).</summary>
    public FlowDirection LayoutDirection => _translator.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    [ObservableProperty]
    public partial Language SelectedLanguage { get; set; }

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

        _statusKey = Saves.Count == 0 ? "vm.noSaves" : null;
        RenderMessages();

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

    partial void OnSelectedLanguageChanged(Language value)
    {
        if (_initialising) return;

        _translator.SetLanguage(value);
        if (_settings is not null) _settings.Language = value.Code;

        OnPropertyChanged(nameof(LayoutDirection));
        RenderMessages();
        if (_report is not null) Report = new IslandReportViewModel(_report, _translator.Localizer);
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
        _error = null;
        RenderMessages();

        try
        {
            var report = await _service.AnalyzeAsync(save.Path);
            if (version != _loadVersion) return; // a newer selection superseded this one

            _report = report;
            Report = new IslandReportViewModel(report, _translator.Localizer);
        }
        catch (Exception e)
        {
            // UI boundary: any failure to read or decode a save is reported to the user instead of crashing the app.
            if (version != _loadVersion) return;

            _report = null;
            Report = null;
            _error = (save.Name, e.Message);
            RenderMessages();
        }
        finally
        {
            if (version == _loadVersion) IsBusy = false;
        }
    }

    // Status and error texts are kept as keys / parts so they can be shown again in another language.
    private void RenderMessages()
    {
        var loc = _translator.Localizer;
        StatusMessage = _statusKey is null ? null : loc.Get(_statusKey);
        ErrorMessage = _error is { } error ? loc.Format("vm.readError", error.Save, error.Detail) : null;
    }
}
