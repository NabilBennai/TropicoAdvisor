using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Tropico.Localization;

namespace Tropico.Desktop.Localization;

/// <summary>
/// The language currently shown by the application. XAML labels bind to its indexer (see <see cref="TExtension"/>): changing the
/// language raises a change notification on the indexer, so every label updates without restarting.
/// </summary>
public sealed class Translator : ObservableObject
{
    private Language _language;
    private ILocalizer _localizer;

    public Translator(Language? language = null)
    {
        _language = language ?? Languages.Default;
        _localizer = Tropico.Localization.Localizer.For(_language);
    }

    /// <summary>The application-wide translator used by the XAML markup extension.</summary>
    public static Translator Instance { get; } = new();

    public Language Language => _language;

    public ILocalizer Localizer => _localizer;

    public bool IsRightToLeft => _language.IsRightToLeft;

    /// <summary>Raised after the language changed and every bound label was refreshed.</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>Translation of a key (the indexer is what XAML bindings use).</summary>
    public string this[string key] => _localizer.Get(key);

    public void SetLanguage(Language language)
    {
        if (language.Code == _language.Code) return;

        _language = language;
        _localizer = Tropico.Localization.Localizer.For(language);

        // "Item[]" refreshes every indexer binding
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(Localizer));
        OnPropertyChanged(nameof(IsRightToLeft));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
