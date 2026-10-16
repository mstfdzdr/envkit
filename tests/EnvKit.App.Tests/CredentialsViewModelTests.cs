using System.Text;
using EnvKit.App.ViewModels;
using EnvKit.Core.Credentials;
using EnvKit.Core.Plans;
using EnvKit.Testing;

namespace EnvKit.App.Tests;

public class CredentialsViewModelTests
{
    private static readonly CredentialKey Git = new("git:https://github.com", CredentialKind.Generic);
    private static readonly CredentialKey Gitlab = new("git:https://gitlab.com", CredentialKind.Generic);
    private static readonly CredentialKey Share = new("fileserver", CredentialKind.DomainPassword);

    private readonly InMemoryEnvStore _store = new();
    private readonly InMemoryCredentialStore _credentials = new();
    private readonly InMemorySnapshotStore _snapshots = new();
    private readonly MainViewModel _main;

    public CredentialsViewModelTests()
    {
        _credentials.Seed(Git, new CredentialState("me", Encoding.UTF8.GetBytes("ghp_secret"), CredentialPersistence.LocalMachine));
        _credentials.Seed(Gitlab, new CredentialState("me", Encoding.Unicode.GetBytes("glpat"), CredentialPersistence.LocalMachine));
        _credentials.Seed(Share, new CredentialState(@"DOMAIN\me", null, CredentialPersistence.Enterprise));
        _main = new MainViewModel(new AppServices
        {
            Store = _store,
            Credentials = _credentials,
            Vault = new InMemoryVault(),
            Profiles = new InMemoryProfileStore(),
            FileSystem = new FakeFileSystem(),
            Files = new InMemoryFileStore(),
            Git = new NoGit(),
            Snapshots = _snapshots,
            Broadcaster = new CountingBroadcaster(),
            SystemFallback = _ => null,
            Elevation = new FakeElevation(_store) { IsElevated = true },
        });
    }

    private CredentialsViewModel Page => _main.Credentials;

    private CredentialRow Row(CredentialKey key) => Page.Rows.Single(r => r.Credential.Key == key);

    [Fact]
    public void Groups_list_counts_and_filter_rows()
    {
        Assert.Equal(["All (3)", "Git (2)", "Network (1)"], Page.Groups.Select(g => g.Label));

        Page.SelectedGroup = Page.Groups.Single(g => g.Group == CredentialGroup.Network);

        Assert.Equal([Share], Page.Rows.Select(r => r.Credential.Key));
    }

    [Fact]
    public void Secret_is_shown_only_on_request_and_hidden_again_on_selection_change()
    {
        Page.Selected = Row(Git);
        Assert.Null(Page.RevealedSecret);

        Page.RevealPasswordCommand.Execute(null);
        Assert.Equal("ghp_secret", Page.RevealedSecret);

        Page.Selected = Row(Gitlab);
        Assert.Null(Page.RevealedSecret);
    }

    [Fact]
    public void Domain_credentials_are_locked_but_deletable()
    {
        Page.Selected = Row(Share);

        Assert.True(Page.IsReadOnlyKind);
        Assert.False(Page.SaveCommand.CanExecute(null));
        Assert.False(Page.RevealPasswordCommand.CanExecute(null));
        Assert.True(Page.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task Editing_with_an_empty_password_keeps_the_secret()
    {
        Page.Selected = Row(Git);
        Page.EditUser = "renamed";

        Page.SaveCommand.Execute(null);
        await _main.Review!.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("renamed", _credentials.Get(Git)!.State.UserName);
        Assert.Equal("ghp_secret", _credentials.Get(Git)!.State.SecretText);
        Assert.DoesNotContain("terminals", _main.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task New_credential_is_created_through_review()
    {
        Page.NewCommand.Execute(null);
        Page.EditTarget = "my-app/token";
        Page.EditUser = "bot";
        Page.EditPassword = "s3cret";

        Page.SaveCommand.Execute(null);
        await _main.Review!.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("s3cret", _credentials.Get(new CredentialKey("my-app/token", CredentialKind.Generic))!.State.SecretText);
        Assert.Contains(Page.Rows, r => r.TargetName == "my-app/token");
    }

    [Fact]
    public async Task Ticked_rows_are_deleted_together_and_restored_from_history()
    {
        Row(Git).IsChecked = true;
        Row(Share).IsChecked = true;
        Assert.Equal(2, Page.DeleteCount);

        Page.DeleteCommand.Execute(null);
        Assert.True(_main.Review!.HasIrreversibleChanges);
        Assert.Equal(2, _main.Review.Plan.Changes.Count);
        await _main.Review.ApplyCommand.ExecuteAsync(null);

        Assert.Equal([Gitlab], Page.Rows.Select(r => r.Credential.Key));

        _main.History.Selected = Assert.Single(_main.History.Snapshots);
        _main.History.RestoreCommand.Execute(null);
        await _main.Review!.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("ghp_secret", _credentials.Get(Git)!.State.SecretText);
        Assert.Null(_credentials.Get(Share));
    }

    [Fact]
    public void Search_matches_targets_and_users_but_not_secrets()
    {
        Page.Search = "gitlab";
        Assert.Equal([Gitlab], Page.Rows.Select(r => r.Credential.Key));

        Page.Search = "ghp_";
        Assert.Empty(Page.Rows);
    }
}
