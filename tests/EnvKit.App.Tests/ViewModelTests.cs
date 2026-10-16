using EnvKit.App.ViewModels;
using EnvKit.Core.Plans;
using EnvKit.Core.Variables;
using EnvKit.Testing;
using Wpf.Ui.Controls;

namespace EnvKit.App.Tests;

public class ViewModelTests
{
    private readonly InMemoryEnvStore _store = new();
    private readonly InMemorySnapshotStore _snapshots = new();
    private readonly CountingBroadcaster _broadcaster = new();

    public ViewModelTests()
    {
        _store.Set(new EnvVariable("Path", @"C:\Windows", EnvScope.Machine, EnvValueKind.Expandable));
        _store.Set(new EnvVariable("Path", @"C:\a;;C:\b;C:\a", EnvScope.User, EnvValueKind.Expandable));
        _store.Set(new EnvVariable("GITHUB_TOKEN", "ghp_secret", EnvScope.User));
    }

    private MainViewModel CreateMain(IEnvStore? store = null) => new(new AppServices
    {
        Store = store ?? _store,
        FileSystem = new FakeFileSystem().WithDirectories(@"C:\Windows", @"C:\a", @"C:\b"),
        Snapshots = _snapshots,
        Broadcaster = _broadcaster,
        SystemFallback = _ => null,
    });

    private string UserPath => _store.Get("Path", EnvScope.User)!.Value;

    [Fact]
    public void Path_page_shows_problems_for_the_working_copy()
    {
        var path = CreateMain().Path;

        Assert.Equal(4, path.Entries.Count);
        Assert.False(path.Entries[0].HasProblems);
        Assert.Equal("empty", path.Entries[1].Problems);
        Assert.Equal("(empty)", path.Entries[1].Display);
        Assert.Equal("duplicate of User#0", path.Entries[3].Problems);
        Assert.False(path.IsDirty);
    }

    [Fact]
    public void Edits_are_reanalyzed_immediately_and_mark_the_page_dirty()
    {
        var path = CreateMain().Path;

        path.FixProblemsCommand.Execute(null);

        Assert.Equal([@"C:\a", @"C:\b"], path.Entries.Select(e => e.Raw));
        Assert.All(path.Entries, e => Assert.False(e.HasProblems));
        Assert.True(path.IsDirty);
        Assert.Equal(@"C:\a;;C:\b;C:\a", UserPath);
    }

    [Fact]
    public void Drag_and_drop_reorders_and_keeps_the_dropped_entry_selected()
    {
        var path = CreateMain().Path;

        path.Move(2, 0);

        Assert.Equal([@"C:\b", @"C:\a", "", @"C:\a"], path.Entries.Select(e => e.Raw));
        Assert.Equal(@"C:\b", path.SelectedEntry?.Raw);
    }

    [Fact]
    public void Scopes_keep_separate_working_copies()
    {
        var path = CreateMain().Path;
        path.NewEntry = @"C:\b";
        path.AddCommand.Execute(null);

        path.Scope = EnvScope.Machine;
        Assert.Equal([@"C:\Windows"], path.Entries.Select(e => e.Raw));

        path.Scope = EnvScope.User;
        Assert.Equal(5, path.Entries.Count);
    }

    [Fact]
    public async Task Save_goes_through_review_and_apply()
    {
        var main = CreateMain();
        main.Path.FixProblemsCommand.Execute(null);

        main.Path.SaveCommand.Execute(null);
        Assert.NotNull(main.Review);
        Assert.Equal(@"C:\a;;C:\b;C:\a", UserPath);

        await main.Review.ApplyCommand.ExecuteAsync(null);

        Assert.Null(main.Review);
        Assert.Equal(@"C:\a;C:\b", UserPath);
        Assert.Single(_snapshots.Saved);
        Assert.Equal(InfoBarSeverity.Success, main.StatusSeverity);
        Assert.False(main.Path.IsDirty);
    }

    [Fact]
    public void Cancelling_the_review_writes_nothing()
    {
        var main = CreateMain();
        main.Path.FixProblemsCommand.Execute(null);
        main.Path.SaveCommand.Execute(null);

        main.Review!.CancelCommand.Execute(null);

        Assert.Null(main.Review);
        Assert.Equal(@"C:\a;;C:\b;C:\a", UserPath);
        Assert.True(main.Path.IsDirty);
    }

    [Fact]
    public async Task Machine_change_without_rights_reports_and_writes_nothing()
    {
        var main = CreateMain(new MachineReadOnlyStore(_store));
        main.Path.Scope = EnvScope.Machine;
        main.Path.NewEntry = @"C:\tools";
        main.Path.AddCommand.Execute(null);
        main.Path.SaveCommand.Execute(null);

        Assert.True(main.Review!.RequiresElevation);
        await main.Review.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(InfoBarSeverity.Error, main.StatusSeverity);
        Assert.Equal(@"C:\Windows", _store.Get("Path", EnvScope.Machine)!.Value);
    }

    [Fact]
    public void Secrets_are_masked_and_locked_until_revealed()
    {
        var variables = CreateMain().Variables;
        var token = variables.Variables.Single(v => v.Name == "GITHUB_TOKEN");

        Assert.Equal("••••••••", token.DisplayValue);
        variables.Selected = token;
        Assert.True(variables.IsValueHidden);
        Assert.False(variables.SaveCommand.CanExecute(null));

        variables.RevealSecrets = true;

        Assert.Equal("ghp_secret", variables.Selected!.DisplayValue);
        Assert.False(variables.IsValueHidden);
        Assert.True(variables.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Search_does_not_match_masked_values()
    {
        var variables = CreateMain().Variables;

        variables.Search = "ghp_";
        Assert.Empty(variables.Variables);

        variables.RevealSecrets = true;
        Assert.Single(variables.Variables);
    }

    [Fact]
    public void New_variable_is_reviewed_with_the_chosen_kind()
    {
        var main = CreateMain();
        main.Variables.NewCommand.Execute(null);
        main.Variables.EditName = "JAVA_HOME";
        main.Variables.EditValue = @"%ProgramFiles%\jdk";
        main.Variables.EditExpandable = true;

        main.Variables.SaveCommand.Execute(null);

        var change = Assert.IsType<VariableChange>(Assert.Single(main.Review!.Plan.Changes));
        Assert.Null(change.Before);
        Assert.Equal(EnvValueKind.Expandable, change.After!.Kind);
    }

    [Fact]
    public async Task History_lists_snapshots_and_previews_a_restore()
    {
        var main = CreateMain();
        main.Path.FixProblemsCommand.Execute(null);
        main.Path.SaveCommand.Execute(null);
        await main.Review!.ApplyCommand.ExecuteAsync(null);

        var history = main.History;
        history.Selected = Assert.Single(history.Snapshots);

        Assert.True(history.CanRestore);
        Assert.Equal(@"User\Path", Assert.Single(history.Preview).Title);

        history.RestoreCommand.Execute(null);
        await main.Review!.ApplyCommand.ExecuteAsync(null);
        Assert.Equal(@"C:\a;;C:\b;C:\a", UserPath);
    }
}
