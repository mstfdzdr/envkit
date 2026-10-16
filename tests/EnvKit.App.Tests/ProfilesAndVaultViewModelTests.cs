using EnvKit.App.ViewModels;
using EnvKit.Core.Profiles;
using EnvKit.Core.Variables;
using EnvKit.Testing;
using Wpf.Ui.Controls;

namespace EnvKit.App.Tests;

public class ProfilesAndVaultViewModelTests
{
    private readonly InMemoryEnvStore _store = new();
    private readonly InMemoryVault _vault = new();
    private readonly InMemoryProfileStore _profiles = new();
    private readonly InMemorySnapshotStore _snapshots = new();
    private readonly MainViewModel _main;

    public ProfilesAndVaultViewModelTests()
    {
        _store.Set(new EnvVariable("Path", @"C:\Windows", EnvScope.User));
        _profiles.Save(new Profile("api", [new SetVariableAction("OPENAI_API_KEY", "${vault:openai}")]));
        _main = new MainViewModel(new AppServices
        {
            Store = _store,
            Credentials = new InMemoryCredentialStore(),
            Vault = _vault,
            Profiles = _profiles,
            FileSystem = new FakeFileSystem(),
            Snapshots = _snapshots,
            Broadcaster = new CountingBroadcaster(),
            SystemFallback = _ => null,
            Elevation = new FakeElevation(_store) { IsElevated = true },
        });
    }

    private ProfilesViewModel Profiles => _main.Profiles;

    private VaultViewModel Vault => _main.Vault;

    [Fact]
    public async Task New_profile_is_saved_then_applied_through_review()
    {
        Profiles.NewCommand.Execute(null);
        Profiles.EditName = "java21";
        Profiles.AddActionCommand.Execute(null);
        Profiles.Actions[0].Name = "JAVA_HOME";
        Profiles.Actions[0].Value = @"C:\jdk21";
        Profiles.AddActionCommand.Execute(null);
        Profiles.Actions[1].Value = @"%JAVA_HOME%\bin";

        Profiles.SaveCommand.Execute(null);
        Assert.Equal(
            [new SetVariableAction("JAVA_HOME", @"C:\jdk21"), new PrependPathAction(@"%JAVA_HOME%\bin")],
            _profiles.Get("java21")!.Actions);
        Assert.Equal("envkit run -p java21 -- ", Profiles.RunCommandText);

        Profiles.ApplyCommand.Execute(null);
        await _main.Review!.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(@"%JAVA_HOME%\bin;C:\Windows", _store.Get("Path", EnvScope.User)!.Value);
        Assert.Single(_snapshots.Saved);
    }

    [Fact]
    public void Vault_profiles_are_run_only_and_missing_secrets_are_pointed_out()
    {
        Profiles.Selected = Profiles.Profiles.Single(p => p.Name == "api");
        Assert.Contains("openai", Profiles.MissingSecrets, StringComparison.Ordinal);

        Profiles.ApplyCommand.Execute(null);

        Assert.Null(_main.Review);
        Assert.Equal(InfoBarSeverity.Warning, _main.StatusSeverity);
        Assert.Contains("envkit run", _main.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_profile_is_reported_not_saved()
    {
        Profiles.NewCommand.Execute(null);
        Profiles.EditName = "bad name";
        Profiles.AddActionCommand.Execute(null);
        Profiles.Actions[0].Name = "X";

        Profiles.SaveCommand.Execute(null);

        Assert.Equal(InfoBarSeverity.Error, _main.StatusSeverity);
        Assert.Null(_profiles.Get("bad name"));
    }

    [Fact]
    public void Renaming_a_profile_replaces_the_old_one()
    {
        Profiles.Selected = Profiles.Profiles.Single(p => p.Name == "api");
        Profiles.EditName = "openai";

        Profiles.SaveCommand.Execute(null);

        Assert.Null(_profiles.Get("api"));
        Assert.NotNull(_profiles.Get("openai"));
    }

    [Fact]
    public void Actions_can_be_reordered()
    {
        Profiles.NewCommand.Execute(null);
        Profiles.AddActionCommand.Execute(null);
        Profiles.AddActionCommand.Execute(null);
        var second = Profiles.Actions[1];

        Profiles.SelectedAction = second;
        Profiles.MoveActionUpCommand.Execute(null);

        Assert.Same(second, Profiles.Actions[0]);
    }

    [Fact]
    public void Vault_stores_reveals_on_request_and_shows_usage()
    {
        Vault.NewCommand.Execute(null);
        Vault.EditName = "openai";
        Vault.EditValue = "sk-123";
        Vault.SaveCommand.Execute(null);

        Assert.Equal("sk-123", _vault.Get("openai"));
        Assert.Equal("openai", Vault.Selected);
        Assert.Null(Vault.RevealedValue);
        Assert.Equal("${vault:openai}", Vault.Reference);
        Assert.Equal("Used by: api", Vault.UsedBy);

        Vault.RevealCommand.Execute(null);
        Assert.Equal("sk-123", Vault.RevealedValue);
    }

    [Fact]
    public void Vault_delete_needs_a_second_click()
    {
        _vault.Set("k", "v");
        Vault.Refresh();
        Vault.Selected = "k";

        Vault.DeleteCommand.Execute(null);
        Assert.NotNull(_vault.Get("k"));
        Assert.Equal("Click again to delete", Vault.DeleteLabel);

        Vault.DeleteCommand.Execute(null);
        Assert.Null(_vault.Get("k"));
    }

    [Fact]
    public void Vault_refuses_to_silently_overwrite_through_new()
    {
        _vault.Set("k", "old");
        Vault.Refresh();
        Vault.NewCommand.Execute(null);
        Vault.EditName = "k";
        Vault.EditValue = "new";

        Vault.SaveCommand.Execute(null);

        Assert.Equal("old", _vault.Get("k"));
        Assert.Equal(InfoBarSeverity.Error, _main.StatusSeverity);
    }
}
