using System.IO;
using EnvKit.App.ViewModels;
using EnvKit.Core.IO;
using EnvKit.Core.Plans;
using EnvKit.Testing;
using Wpf.Ui.Controls;

namespace EnvKit.App.Tests;

public sealed class ProjectSecretsViewModelTests : IDisposable
{
    private const string Original = "# app\nPORT=3000\nAPI_KEY=sk-live-1   # rotate\nPORT=3001\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "EnvKit.Tests", Guid.NewGuid().ToString("N"));
    private readonly InMemoryEnvStore _store = new();
    private readonly InMemoryVault _vault = new();
    private readonly MainViewModel _main;

    public ProjectSecretsViewModelTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, ".env"), Original);
        File.WriteAllText(Path.Combine(_root, "Api.csproj"), "<Project><PropertyGroup><UserSecretsId>vm-test</UserSecretsId></PropertyGroup></Project>");
        _main = new MainViewModel(new AppServices
        {
            Store = _store,
            Credentials = new InMemoryCredentialStore(),
            Vault = _vault,
            Profiles = new InMemoryProfileStore(),
            FileSystem = new FakeFileSystem(),
            Files = new PhysicalFileStore(),
            Git = new NoGit(),
            Snapshots = new InMemorySnapshotStore(),
            Broadcaster = new CountingBroadcaster(),
            SystemFallback = _ => null,
            Elevation = new FakeElevation(_store) { IsElevated = true },
        });
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ProjectSecretsViewModel Page => _main.ProjectSecrets;

    private string EnvText => File.ReadAllText(Path.Combine(_root, ".env"));

    private async Task ScanAsync()
    {
        Page.Folder = _root;
        await Page.ScanCommand.ExecuteAsync(null);
        Page.SelectedFile = Page.Files.Single(f => f.File.Format == FileFormat.DotEnv);
    }

    [Fact]
    public async Task Scan_lists_files_and_shows_masked_values_with_problems()
    {
        await ScanAsync();

        Assert.Equal(2, Page.Files.Count);
        Assert.Contains(Page.Files, f => f.Title == "User secrets · Api.csproj");
        Assert.Equal(["PORT", "API_KEY"], Page.Entries.Select(e => e.Key));
        Assert.Equal("3001", Page.Entries[0].DisplayValue);
        Assert.Equal("••••••••", Page.Entries[1].DisplayValue);
        Assert.Contains("PORT is assigned more than once", Page.Problems, StringComparison.Ordinal);

        Page.RevealSecrets = true;
        Assert.Equal("sk-live-1", Page.Entries[1].DisplayValue);
    }

    [Fact]
    public async Task Row_text_used_for_ui_automation_never_contains_the_value()
    {
        await ScanAsync();

        Assert.All(Page.Entries, e => Assert.DoesNotContain("sk-live-1", e.ToString(), StringComparison.Ordinal));
        Assert.Equal("API_KEY", Page.Entries[1].ToString());
    }

    [Fact]
    public async Task Saving_goes_through_review_and_keeps_the_rest_of_the_file()
    {
        await ScanAsync();
        Page.SelectedEntry = Page.Entries[0];
        Page.EditValue = "8080";

        Page.SaveCommand.Execute(null);
        await _main.Review!.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("# app\nPORT=3000\nAPI_KEY=sk-live-1   # rotate\nPORT=8080\n", EnvText);
        Assert.Equal("8080", Page.Entries[0].Value);
    }

    [Fact]
    public async Task Masked_secret_is_not_cleared_by_saving_an_empty_value()
    {
        await ScanAsync();
        Page.SelectedEntry = Page.Entries[1];
        Assert.Equal(string.Empty, Page.EditValue);

        Page.SaveCommand.Execute(null);

        Assert.Null(_main.Review);
        Assert.Equal(Original, EnvText);
    }

    [Fact]
    public async Task Moving_to_the_vault_happens_only_after_confirmation()
    {
        await ScanAsync();
        Page.SelectedEntry = Page.Entries[1];
        Assert.Equal("api_key", Page.VaultName);

        Page.MoveToVaultCommand.Execute(null);
        _main.Review!.CancelCommand.Execute(null);
        Assert.Null(_vault.Get("api_key"));
        Assert.Equal(Original, EnvText);

        Page.MoveToVaultCommand.Execute(null);
        await _main.Review!.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("sk-live-1", _vault.Get("api_key"));
        Assert.Contains("API_KEY=${vault:api_key}   # rotate", EnvText, StringComparison.Ordinal);
        Assert.False(Page.MoveToVaultCommand.CanExecute(null));
    }

    [Fact]
    public async Task Files_that_are_not_utf8_are_read_only()
    {
        File.WriteAllBytes(Path.Combine(_root, ".env"), [(byte)'A', (byte)'=', 0xC3, 0x28]);

        await ScanAsync();

        Assert.True(Page.IsReadOnly);
        Assert.Contains("read-only", Page.Problems, StringComparison.Ordinal);
        Page.EditKey = "B";
        Assert.False(Page.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Missing_folder_is_reported()
    {
        Page.Folder = Path.Combine(_root, "nope");

        await Page.ScanCommand.ExecuteAsync(null);

        Assert.Contains("does not exist", Page.ScanSummary, StringComparison.Ordinal);
        Assert.NotEqual(InfoBarSeverity.Error, _main.StatusSeverity);
    }
}
