using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Tropico.Localization;

namespace Tropico.Desktop.Localization;

/// <summary>Remembers the chosen language between runs.</summary>
public interface ISettingsStore
{
    string? Language { get; set; }
}

/// <summary>Settings kept in <c>%APPDATA%\TropicoAdvisor\settings.json</c>. Read and write errors never stop the application.</summary>
public sealed class FileSettingsStore(string? path = null) : ISettingsStore
{
    private readonly string _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TropicoAdvisor", "settings.json");

    private sealed record Data(string? Language);

    public string? Language
    {
        get
        {
            try
            {
                return File.Exists(_path) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(_path))?.Language : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                return null;
            }
        }
        set
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(new Data(value)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // the preference is a convenience: losing it must not break the application
            }
        }
    }
}

public static class LanguageResolver
{
    public const string EnvironmentVariable = "TROPICO_LANGUAGE";

    /// <summary>
    /// The language to start with: the environment override, then the saved choice, then the system UI language, then English.
    /// An unsupported code at any step falls through to the next one.
    /// </summary>
    public static Language Resolve(ISettingsStore? settings, CultureInfo systemCulture, string? environmentOverride = null) =>
        Languages.Find(environmentOverride)
        ?? Languages.Find(settings?.Language)
        ?? Languages.Find(systemCulture.TwoLetterISOLanguageName)
        ?? Languages.Default;
}
