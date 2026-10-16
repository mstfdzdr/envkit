using EnvKit.Core.Profiles;
using EnvKit.Core.Variables;
using EnvKit.Testing;

namespace EnvKit.Cli.Tests;

public sealed class ProfileCliTests : IDisposable
{
    private readonly InMemoryEnvStore _store = new();
    private readonly InMemoryVault _vault = new();
    private readonly InMemoryProfileStore _profiles = new();
    private readonly RecordingProcessRunner _processes = new() { ExitCode = 7 };
    private readonly InMemorySnapshotStore _snapshots = new();
    private readonly StringWriter _output = new();
    private readonly Prompt _prompt = new();
    private readonly FakeFileSystem _fileSystem = new FakeFileSystem().WithFiles(@"C:\jdk21\bin\java.exe", @"C:\Windows\cmd.exe");

    public ProfileCliTests() => _store.Set(new EnvVariable("Path", @"C:\Windows", EnvScope.User));

    public void Dispose() => _output.Dispose();

    private string Output => _output.ToString();

    private int Run(params string[] args) => RunWithInput(string.Empty, args);

    private int RunWithInput(string input, params string[] args)
    {
        using var reader = new StringReader(input);
        return EnvKitCli.Run(
            new CliServices
            {
                Store = _store,
                Credentials = new InMemoryCredentialStore(),
                Vault = _vault,
                Profiles = _profiles,
                Processes = _processes,
                ProcessEnvironment = () => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Path"] = @"C:\Windows",
                    ["PATHEXT"] = ".EXE;.CMD",
                },
                FileSystem = _fileSystem,
                Snapshots = _snapshots,
                Broadcaster = new CountingBroadcaster(),
                SystemFallback = _ => null,
                In = reader,
                Out = _output,
                Error = _output,
                Prompt = _prompt,
                Elevation = new FakeElevation(_store) { IsElevated = true },
            },
            args);
    }

    [Fact]
    public void New_profile_orders_actions_and_lists()
    {
        Assert.Equal(0, Run("profile", "new", "java21", "--set", @"JAVA_HOME=C:\jdk21", "--prepend-path", @"%JAVA_HOME%\bin", "-d", "Temurin"));

        Assert.Equal(
            [new SetVariableAction("JAVA_HOME", @"C:\jdk21"), new PrependPathAction(@"%JAVA_HOME%\bin")],
            _profiles.Get("java21")!.Actions);

        Run("profile", "list");
        Assert.Contains("java21  [2 action(s)]  Temurin", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void New_refuses_to_overwrite_without_force_and_needs_actions()
    {
        Run("profile", "new", "p", "--unset", "X");

        Assert.Equal(1, Run("profile", "new", "p", "--unset", "Y"));
        Assert.Equal(0, Run("profile", "new", "p", "--unset", "Y", "--force"));
        Assert.Equal(1, Run("profile", "new", "empty"));
        Assert.Equal(1, Run("profile", "new", "bad", "--set", "NOEQUALS"));
    }

    [Fact]
    public void Apply_goes_through_a_plan()
    {
        Run("profile", "new", "java21", "--set", @"JAVA_HOME=C:\jdk21", "--prepend-path", @"%JAVA_HOME%\bin");

        Assert.Equal(0, Run("profile", "apply", "java21", "--yes"));

        Assert.Equal(@"C:\jdk21", _store.Get("JAVA_HOME", EnvScope.User)!.Value);
        Assert.Equal(@"%JAVA_HOME%\bin;C:\Windows", _store.Get("Path", EnvScope.User)!.Value);
        Assert.Single(_snapshots.Saved);
    }

    [Fact]
    public void Profiles_using_the_vault_cannot_be_applied()
    {
        Run("profile", "new", "api", "--set", "OPENAI_API_KEY=${vault:openai}");
        Assert.Contains("not in the vault yet: openai", Output, StringComparison.Ordinal);

        Assert.Equal(1, Run("profile", "apply", "api", "--yes"));
        Assert.Contains("envkit run --profile api", Output, StringComparison.Ordinal);
        Assert.Null(_store.Get("OPENAI_API_KEY", EnvScope.User));
    }

    [Fact]
    public void Run_applies_profiles_and_secrets_to_the_child_only()
    {
        _vault.Set("openai", "sk-123");
        _vault.Set("gh", "ghp_456");
        Run("profile", "new", "java21", "--set", @"JAVA_HOME=C:\jdk21", "--set", "OPENAI_API_KEY=${vault:openai}", "--prepend-path", @"%JAVA_HOME%\bin");

        var exit = Run("run", "-p", "java21", "-s", "GITHUB_TOKEN=gh", "--", "java", "-version", "--flag");

        Assert.Equal(7, exit);
        Assert.Equal(@"C:\jdk21\bin\java.EXE", _processes.FileName, ignoreCase: true);
        Assert.Equal(["-version", "--flag"], _processes.Arguments);
        Assert.Equal("sk-123", _processes.Environment["OPENAI_API_KEY"]);
        Assert.Equal("ghp_456", _processes.Environment["GITHUB_TOKEN"]);
        Assert.Equal(@"C:\jdk21\bin;C:\Windows", _processes.Environment["Path"]);
        Assert.Null(_store.Get("OPENAI_API_KEY", EnvScope.User));
        Assert.DoesNotContain("sk-123", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_fails_before_starting_when_a_secret_or_command_is_missing()
    {
        Assert.Equal(1, Run("run", "-s", "TOKEN", "--", "cmd"));
        Assert.Contains("Not in the vault: TOKEN", Output, StringComparison.Ordinal);

        Assert.Equal(1, Run("run", "--", "nope"));
        Assert.Contains("nope: not found", Output, StringComparison.Ordinal);
        Assert.Null(_processes.FileName);
    }

    [Fact]
    public void Vault_set_prompts_and_list_shows_names_only()
    {
        _prompt.Secret = "sk-123";

        Assert.Equal(0, Run("vault", "set", "openai"));
        Run("vault", "list");

        Assert.Equal("sk-123", _vault.Get("openai"));
        Assert.Contains("openai", Output, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-123", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Vault_set_from_stdin_replaces_only_when_confirmed()
    {
        _vault.Set("k", "old");
        _prompt.IsInteractive = false;

        Assert.Equal(5, RunWithInput("new\n", "vault", "set", "k", "--value-stdin"));
        Assert.Equal("old", _vault.Get("k"));

        Assert.Equal(0, RunWithInput("new\n", "vault", "set", "k", "--value-stdin", "--yes"));
        Assert.Equal("new", _vault.Get("k"));
    }

    [Fact]
    public void Byte_order_mark_from_a_powershell_pipe_is_not_part_of_the_secret()
    {
        Assert.Equal(0, RunWithInput("﻿sk-123\n", "vault", "set", "k", "--value-stdin"));

        Assert.Equal("sk-123", _vault.Get("k"));
    }

    [Fact]
    public void Vault_delete_asks_first()
    {
        _vault.Set("k", "v");
        _prompt.Answer = false;

        Assert.Equal(5, Run("vault", "delete", "k"));
        Assert.NotNull(_vault.Get("k"));

        Assert.Equal(0, Run("vault", "delete", "k", "--yes"));
        Assert.Null(_vault.Get("k"));
    }

    [Fact]
    public void Import_and_export_round_trip()
    {
        var file = Path.Combine(Path.GetTempPath(), $"envkit-profile-{Guid.NewGuid():N}.json");
        try
        {
            Run("profile", "new", "node", "--append-path", @"C:\nodejs");
            Assert.Equal(0, Run("profile", "export", "node", file));
            _profiles.Delete("node");

            Assert.Equal(0, Run("profile", "import", file));
            Assert.Equal([new AppendPathAction(@"C:\nodejs")], _profiles.Get("node")!.Actions);

            File.WriteAllText(file, "{ broken");
            Assert.Equal(1, Run("profile", "import", file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    private sealed class Prompt : IPrompt
    {
        public bool IsInteractive { get; set; } = true;

        public bool Answer { get; set; } = true;

        public string? Secret { get; set; }

        public bool Confirm(string question) => Answer;

        public string? ReadSecret(string label) => Secret;
    }
}
