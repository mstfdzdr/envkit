using EnvKit.Core.Variables;
using EnvKit.Testing;

namespace EnvKit.Cli.Tests;

public sealed class CliTests : IDisposable
{
    private readonly InMemoryEnvStore _store = new();
    private readonly InMemorySnapshotStore _snapshots = new();
    private readonly CountingBroadcaster _broadcaster = new();
    private readonly FakeFileSystem _fileSystem = new FakeFileSystem()
        .WithDirectories(@"C:\a", @"C:\b")
        .WithFiles(@"C:\a\tool.exe", @"C:\b\tool.exe");

    private readonly FakePrompt _prompt = new();
    private readonly StringWriter _output = new();

    public CliTests()
    {
        _store.Set(new EnvVariable("Path", @"C:\Windows", EnvScope.Machine, EnvValueKind.Expandable));
        _store.Set(new EnvVariable("Path", @"C:\a;;C:\b;C:\A\", EnvScope.User, EnvValueKind.Expandable));
    }

    public void Dispose() => _output.Dispose();

    private string Output => _output.ToString();

    private string UserPath => _store.Get("Path", EnvScope.User)!.Value;

    private int Run(params string[] args) => EnvKitCli.Run(
        new CliServices
        {
            Store = _store,
            FileSystem = _fileSystem,
            Snapshots = _snapshots,
            Broadcaster = _broadcaster,
            SystemFallback = _ => null,
            Out = _output,
            Error = _output,
            Prompt = _prompt,
        },
        args);

    [Fact]
    public void Path_list_check_reports_problems_and_fails()
    {
        Assert.Equal(1, Run("path", "list", "--check", "--scope", "user"));

        Assert.Contains("[empty]", Output, StringComparison.Ordinal);
        Assert.Contains("[duplicate of User#0]", Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Machine PATH", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Dry_run_shows_the_diff_and_writes_nothing()
    {
        Assert.Equal(0, Run("path", "add", @"C:\new", "--dry-run"));

        Assert.Contains(@"+ C:\new", Output, StringComparison.Ordinal);
        Assert.Contains("does not exist", Output, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\new", UserPath, StringComparison.Ordinal);
        Assert.Empty(_snapshots.Saved);
    }

    [Fact]
    public void Redirected_input_without_yes_refuses_to_apply()
    {
        _prompt.IsInteractive = false;

        Assert.Equal(5, Run("path", "add", @"C:\new"));
        Assert.DoesNotContain(@"C:\new", UserPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Interactive_confirmation_controls_the_write()
    {
        _prompt.Answer = false;
        Assert.Equal(5, Run("path", "add", @"C:\new"));
        Assert.DoesNotContain(@"C:\new", UserPath, StringComparison.Ordinal);

        _prompt.Answer = true;
        Assert.Equal(0, Run("path", "add", @"C:\new", "--prepend"));
        Assert.StartsWith(@"C:\new;", UserPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Applying_saves_a_snapshot_and_prints_the_undo_command()
    {
        Assert.Equal(0, Run("var", "set", "JAVA_HOME", @"C:\jdk", "--yes"));

        var snapshot = Assert.Single(_snapshots.Saved);
        Assert.Contains($"envkit snapshot restore {snapshot.Id}", Output, StringComparison.Ordinal);
        Assert.Equal(1, _broadcaster.Count);
    }

    [Fact]
    public void Path_fix_removes_empty_and_duplicate_entries()
    {
        Assert.Equal(0, Run("path", "fix", "--yes"));

        Assert.Equal(@"C:\a;C:\b", UserPath);
    }

    [Fact]
    public void Path_remove_matches_normalized_spelling()
    {
        Assert.Equal(0, Run("path", "remove", @"c:\a\", "--yes"));
        Assert.Equal(@";C:\b", UserPath);

        Assert.Equal(1, Run("path", "remove", @"C:\nope", "--yes"));
    }

    [Fact]
    public void Which_lists_the_winner_first()
    {
        Assert.Equal(0, Run("which", "tool"));

        var lines = Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith(@"-> C:\a\tool.exe", lines[0], StringComparison.Ordinal);
        Assert.Contains("shadowed", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Var_set_infers_and_overrides_the_kind()
    {
        Run("var", "set", "REF", "%USERPROFILE%", "--yes");
        Run("var", "set", "LIT", "%USERPROFILE%", "--literal", "--yes");

        Assert.Equal(EnvValueKind.Expandable, _store.Get("REF", EnvScope.User)!.Kind);
        Assert.Equal(EnvValueKind.Literal, _store.Get("LIT", EnvScope.User)!.Kind);
    }

    [Fact]
    public void Var_set_rejects_conflicting_kind_flags()
    {
        Assert.NotEqual(0, Run("var", "set", "X", "1", "--expand", "--literal", "--yes"));
        Assert.Null(_store.Get("X", EnvScope.User));
    }

    [Fact]
    public void Var_list_masks_secrets_unless_asked()
    {
        _store.Set(new EnvVariable("GITHUB_TOKEN", "ghp_do_not_print", EnvScope.User));

        Run("var", "list", "--scope", "user");
        Assert.DoesNotContain("ghp_do_not_print", Output, StringComparison.Ordinal);
        Assert.Contains("GITHUB_TOKEN = ********", Output, StringComparison.Ordinal);

        Run("var", "list", "--scope", "user", "--show-secrets");
        Assert.Contains("ghp_do_not_print", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Var_get_with_scope_prints_only_the_value()
    {
        Run("var", "get", "Path", "--scope", "machine");

        Assert.Equal(@"C:\Windows" + Environment.NewLine, Output);
    }

    [Fact]
    public void Machine_change_without_rights_reports_elevation_and_changes_nothing()
    {
        var store = new MachineReadOnlyStore(_store);
        var exit = EnvKitCli.Run(
            new CliServices
            {
                Store = store,
                FileSystem = _fileSystem,
                Snapshots = _snapshots,
                Broadcaster = _broadcaster,
                SystemFallback = _ => null,
                Out = _output,
                Error = _output,
                Prompt = _prompt,
            },
            ["var", "set", "M", "1", "--scope", "machine", "--yes"]);

        Assert.Equal(3, exit);
        Assert.Contains("administrator", Output, StringComparison.Ordinal);
        Assert.Null(_store.Get("M", EnvScope.Machine));
    }

    [Fact]
    public void Snapshot_restore_undoes_a_change()
    {
        var before = UserPath;
        Run("path", "fix", "--yes");
        var id = Assert.Single(_snapshots.Saved).Id;

        Assert.Equal(0, Run("snapshot", "restore", id, "--scope", "user", "--yes"));

        Assert.Equal(before, UserPath);
    }

    [Fact]
    public void Unknown_snapshot_fails_cleanly() =>
        Assert.Equal(1, Run("snapshot", "diff", "20260101-000000-0000000-abcdef"));

    private sealed class FakePrompt : IPrompt
    {
        public bool IsInteractive { get; set; } = true;

        public bool Answer { get; set; } = true;

        public bool Confirm(string question) => Answer;
    }
}
