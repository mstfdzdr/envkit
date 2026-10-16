using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using EnvKit.App.Localization;
using EnvKit.App.Settings;
using EnvKit.App.ViewModels;
using EnvKit.Core.Variables;
using EnvKit.Testing;

namespace EnvKit.App.Tests;

public sealed partial class SettingsAndLanguageTests : IDisposable
{
    private readonly InMemoryEnvStore _store = new();
    private readonly InMemorySettingsStore _settings = new();
    private readonly RecordingTheme _theme = new();
    private readonly RecordingShell _shell = new();
    private readonly MainViewModel _main;

    public SettingsAndLanguageTests()
    {
        _store.Set(new EnvVariable("Path", @"C:\a;;C:\a", EnvScope.User));
        _main = new MainViewModel(new AppServices
        {
            Store = _store,
            Credentials = new InMemoryCredentialStore(),
            Vault = new InMemoryVault(),
            Profiles = new InMemoryProfileStore(),
            FileSystem = new FakeFileSystem().WithDirectories(@"C:\a"),
            Files = new InMemoryFileStore(),
            Git = new NoGit(),
            Snapshots = new InMemorySnapshotStore(),
            Broadcaster = new CountingBroadcaster(),
            SystemFallback = _ => null,
            Elevation = new FakeElevation(_store) { IsElevated = true },
            SettingsStore = _settings,
            Theme = _theme,
            Shell = _shell,
        });
    }

    public void Dispose() => Loc.Instance.SetCulture(Loc.English);

    [Fact]
    public void Switching_language_updates_titles_and_core_texts_and_is_saved()
    {
        Assert.Equal("Variables", _main.Variables.Title);

        _main.Settings.SelectedLanguage = LanguageChoice.Turkish;

        Assert.Equal("Değişkenler", _main.Variables.Title);
        Assert.Equal("Ayarlar", _main.Settings.Title);
        Assert.Contains("boş", _main.Path.Entries[1].Problems, StringComparison.Ordinal);
        Assert.Equal(LanguageChoice.Turkish, _settings.Current.Language);
        Assert.Equal("Değişkenler", Loc.Instance["Page_Variables"]);
    }

    [Fact]
    public void Switching_language_keeps_unsaved_path_edits()
    {
        _main.Path.FixProblemsCommand.Execute(null);
        Assert.True(_main.Path.IsDirty);

        _main.Settings.SelectedLanguage = LanguageChoice.Turkish;

        Assert.True(_main.Path.IsDirty);
        Assert.Equal([@"C:\a"], _main.Path.Entries.Select(e => e.Raw));
        Assert.Contains("sorun yok", _main.Path.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Theme_choice_is_applied_and_saved()
    {
        _main.Settings.SelectedTheme = ThemeChoice.Dark;

        Assert.Equal([ThemeChoice.Dark], _theme.Applied);
        Assert.Equal(ThemeChoice.Dark, _settings.Current.Theme);
    }

    [Fact]
    public void Saved_settings_are_shown_without_being_reapplied()
    {
        _settings.Save(new AppSettings(ThemeChoice.Light, LanguageChoice.English));

        _main.Settings.Refresh();

        Assert.Equal(ThemeChoice.Light, _main.Settings.SelectedTheme);
        Assert.Empty(_theme.Applied);
    }

    [Fact]
    public void About_links_open_the_profile_and_repository()
    {
        _main.Settings.OpenLinkCommand.Execute(_main.Settings.AuthorProfileUrl);
        _main.Settings.OpenLinkCommand.Execute(_main.Settings.RepositoryUrl);

        Assert.Equal(["https://github.com/mstfdzdr", "https://github.com/mstfdzdr/envkit"], _shell.Opened);
        Assert.StartsWith("Version ", _main.Settings.VersionText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_check_outside_an_installation_explains_itself()
    {
        await _main.Settings.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Contains("installed app", _main.Settings.UpdateStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void Enum_values_are_localized()
    {
        Assert.Equal("Machine", EnumTextConverter.Text(EnvScope.Machine));
        Loc.Instance.SetCulture(Loc.Turkish);
        Assert.Equal("Sistem", EnumTextConverter.Text(EnvScope.Machine));
    }

    [Fact]
    public void Settings_file_round_trips_and_survives_corruption()
    {
        var path = Path.Combine(Path.GetTempPath(), "EnvKit.Tests", Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var store = new FileSettingsStore(path);
            Assert.Equal(new AppSettings(), store.Load());

            store.Save(new AppSettings(ThemeChoice.Dark, LanguageChoice.Turkish));
            Assert.Equal(new AppSettings(ThemeChoice.Dark, LanguageChoice.Turkish), store.Load());

            File.WriteAllText(path, "{ not json");
            Assert.Equal(new AppSettings(), store.Load());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    /// <summary>A mistyped key shows up in the UI as the key itself; catch it here instead.</summary>
    [Fact]
    public void Every_key_used_in_code_and_xaml_exists_in_both_languages()
    {
        var root = RepositoryRoot();
        var app = Path.Combine(root, "src", "EnvKit.App");
        var english = Keys(Path.Combine(app, "Resources", "Strings.resx"));
        var turkish = Keys(Path.Combine(app, "Resources", "Strings.tr.resx"));
        Assert.Equal(english.Order(), turkish.Order());

        var used = Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".xaml", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => KeyReference().Matches(File.ReadAllText(f)).Select(m => m.Groups["key"].Value))
            .ToHashSet();

        Assert.NotEmpty(used);
        var missing = used.Where(k => !english.Contains(k)).Order().ToList();
        Assert.True(missing.Count == 0, $"Missing resource keys: {string.Join(", ", missing)}");
    }

    private static HashSet<string> Keys(string resx) =>
        [.. XDocument.Load(resx).Root!.Elements("data").Select(d => (string)d.Attribute("name")!)];

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EnvKit.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    // {l:Tr Key}, Loc.Get("Key"), Loc.Format("Key", ...)
    [GeneratedRegex(@"(?:l:Tr\s+|Loc\.(?:Get|Format)\(\s*"")(?<key>[A-Za-z]+_[A-Za-z0-9_]+)")]
    private static partial Regex KeyReference();

    private sealed class RecordingTheme : IThemeService
    {
        public List<ThemeChoice> Applied { get; } = [];

        public void Apply(ThemeChoice theme) => Applied.Add(theme);
    }

    private sealed class RecordingShell : IShell
    {
        public List<string> Opened { get; } = [];

        public void Open(string target) => Opened.Add(target);
    }
}
