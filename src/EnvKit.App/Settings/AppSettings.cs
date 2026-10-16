using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using EnvKit.Core;

namespace EnvKit.App.Settings;

public enum ThemeChoice
{
    /// <summary>Follow Windows, including later changes.</summary>
    System,
    Light,
    Dark,
}

public enum LanguageChoice
{
    /// <summary>Turkish on Turkish Windows, otherwise English.</summary>
    System,
    English,
    Turkish,
}

public sealed record AppSettings(ThemeChoice Theme = ThemeChoice.System, LanguageChoice Language = LanguageChoice.System);

public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}

/// <summary>A small JSON file next to the snapshots; a missing or broken file means defaults.</summary>
public sealed class FileSettingsStore(string path) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath => Path.Combine(EnvKitPaths.DataDirectory, "settings.json");

    public AppSettings Load()
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}

public sealed class InMemorySettingsStore : ISettingsStore
{
    public AppSettings Current { get; private set; } = new();

    public AppSettings Load() => Current;

    public void Save(AppSettings settings) => Current = settings;
}
