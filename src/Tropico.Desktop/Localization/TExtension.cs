using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Tropico.Desktop.Localization;

/// <summary>
/// XAML markup extension for translated labels: <c>Text="{loc:T ui.overview}"</c>. It binds to the translator's indexer, so the label
/// follows language changes.
/// </summary>
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding($"[{Key}]")
    {
        Source = Translator.Instance,
        Mode = BindingMode.OneWay,
    };
}
