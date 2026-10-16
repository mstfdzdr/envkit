using System.Text;
using EnvKit.Core.Credentials;
using EnvKit.Testing;

namespace EnvKit.Cli.Tests;

public sealed class CredCliTests : IDisposable
{
    private static readonly CredentialKey Git = new("git:https://github.com", CredentialKind.Generic);
    private static readonly CredentialKey Gitlab = new("git:https://gitlab.com", CredentialKind.Generic);
    private static readonly CredentialKey Share = new("fileserver", CredentialKind.DomainPassword);

    private readonly InMemoryEnvStore _store = new();
    private readonly InMemoryCredentialStore _credentials = new();
    private readonly InMemorySnapshotStore _snapshots = new();
    private readonly StringWriter _output = new();
    private readonly SecretPrompt _prompt = new();

    public CredCliTests()
    {
        _credentials.Seed(Git, new CredentialState("me", Encoding.UTF8.GetBytes("ghp_secret_token"), CredentialPersistence.LocalMachine, "from GCM"));
        _credentials.Seed(Gitlab, new CredentialState("me", Encoding.Unicode.GetBytes("glpat-other"), CredentialPersistence.LocalMachine));
        _credentials.Seed(Share, new CredentialState(@"DOMAIN\me", null, CredentialPersistence.Enterprise));
    }

    public void Dispose() => _output.Dispose();

    private string Output => _output.ToString();

    private int RunWithInput(string input, params string[] args)
    {
        using var reader = new StringReader(input);
        return EnvKitCli.Run(
            new CliServices
            {
                Store = _store,
                Credentials = _credentials,
                FileSystem = new FakeFileSystem(),
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

    private int Run(params string[] args) => RunWithInput(string.Empty, args);

    [Fact]
    public void List_groups_by_tool_and_never_prints_secrets()
    {
        Assert.Equal(0, Run("cred", "list"));

        Assert.Contains("Git:", Output, StringComparison.Ordinal);
        Assert.Contains("Network:", Output, StringComparison.Ordinal);
        Assert.Contains("fileserver [DomainPassword]", Output, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_secret_token", Output, StringComparison.Ordinal);
        Assert.Contains("3 credential(s).", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void List_filters_by_group()
    {
        Run("cred", "list", "--group", "network");

        Assert.Contains("1 credential(s).", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Show_masks_the_secret_unless_revealed()
    {
        Run("cred", "show", "git:https://github.com");
        Assert.DoesNotContain("ghp_secret_token", Output, StringComparison.Ordinal);
        Assert.Contains("Secret:      ********", Output, StringComparison.Ordinal);

        Run("cred", "show", "git:https://github.com", "--reveal");
        Assert.Contains("ghp_secret_token", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Set_prompts_for_the_password_and_keeps_the_tools_encoding()
    {
        _prompt.Secret = "ghp_rotated";

        Assert.Equal(0, Run("cred", "set", "git:https://github.com", "--yes"));

        var state = _credentials.Get(Git)!.State;
        Assert.Equal(Encoding.UTF8.GetBytes("ghp_rotated"), state.Secret);
        Assert.Equal("me", state.UserName);
        Assert.Equal("from GCM", state.Comment);
        Assert.Contains("password: changed", Output, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_rotated", Output, StringComparison.Ordinal);
        Assert.DoesNotContain("New terminals", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Set_reads_stdin_when_asked_and_creates_new_credentials()
    {
        Assert.Equal(0, RunWithInput("s3cret\n", "cred", "set", "my-app/token", "--user", "bot", "--password-stdin", "--yes"));

        var created = _credentials.Get(new CredentialKey("my-app/token", CredentialKind.Generic))!.State;
        Assert.Equal("s3cret", created.SecretText);
        Assert.Equal("bot", created.UserName);
    }

    [Fact]
    public void Empty_password_keeps_the_current_one()
    {
        _prompt.Secret = string.Empty;

        Run("cred", "set", "git:https://github.com", "--user", "other", "--yes");

        Assert.Equal("ghp_secret_token", _credentials.Get(Git)!.State.SecretText);
        Assert.Equal("other", _credentials.Get(Git)!.State.UserName);
    }

    [Fact]
    public void Set_without_a_terminal_requires_password_stdin()
    {
        _prompt.IsInteractive = false;

        Assert.Equal(5, Run("cred", "set", "x", "--yes"));
        Assert.Null(_credentials.Get(new CredentialKey("x", CredentialKind.Generic)));
    }

    [Fact]
    public void Bulk_delete_by_filter_is_undoable()
    {
        Assert.Equal(0, Run("cred", "delete", "--filter", "git:*", "--yes"));
        Assert.Null(_credentials.Get(Git));
        Assert.Null(_credentials.Get(Gitlab));
        Assert.NotNull(_credentials.Get(Share));

        Assert.Equal(0, Run("snapshot", "restore", Assert.Single(_snapshots.Saved).Id, "--yes"));

        Assert.Equal("ghp_secret_token", _credentials.Get(Git)!.State.SecretText);
        Assert.Equal("glpat-other", _credentials.Get(Gitlab)!.State.SecretText);
    }

    [Fact]
    public void Deleting_a_domain_credential_warns_it_is_permanent()
    {
        Run("cred", "delete", "fileserver", "--type", "DomainPassword", "--dry-run");

        Assert.Contains("cannot be restored", Output, StringComparison.Ordinal);
        Assert.NotNull(_credentials.Get(Share));
    }

    [Fact]
    public void Delete_needs_a_selector_and_existing_targets()
    {
        Assert.Equal(1, Run("cred", "delete", "--yes"));
        Assert.Equal(1, Run("cred", "delete", "nope", "--yes"));
        Assert.Equal(3, _credentials.List().Count);
    }

    [Fact]
    public void Export_writes_an_inventory_without_secrets()
    {
        var file = Path.Combine(Path.GetTempPath(), $"envkit-export-{Guid.NewGuid():N}.json");
        try
        {
            Assert.Equal(0, Run("cred", "export", file));

            var json = File.ReadAllText(file);
            Assert.Contains("git:https://github.com", json, StringComparison.Ordinal);
            Assert.DoesNotContain("ghp_secret_token", json, StringComparison.Ordinal);
            Assert.DoesNotContain("glpat-other", json, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private sealed class SecretPrompt : IPrompt
    {
        public bool IsInteractive { get; set; } = true;

        public string? Secret { get; set; }

        public bool Confirm(string question) => true;

        public string? ReadSecret(string label) => Secret;
    }
}
