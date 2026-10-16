using System.Text;
using EnvKit.Core.DotEnv;
using EnvKit.Testing;

namespace EnvKit.Cli.Tests;

public sealed class DotEnvCliTests : IDisposable
{
    private const string Original = "# local settings\nPORT=3000\nAPI_KEY=sk-live-123   # rotate monthly\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "EnvKit.Tests", Guid.NewGuid().ToString("N"));
    private readonly InMemoryEnvStore _store = new();
    private readonly InMemoryVault _vault = new();
    private readonly InMemoryFileStore _files = new();
    private readonly InMemorySnapshotStore _snapshots = new();
    private readonly RecordingProcessRunner _processes = new();
    private readonly StringWriter _output = new();
    private readonly Prompt _prompt = new();
    private readonly string _env;

    public DotEnvCliTests()
    {
        Directory.CreateDirectory(_root);
        _env = Path.Combine(_root, ".env");
        _files.Seed(_env, Original);
    }

    public void Dispose()
    {
        _output.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private string Output => _output.ToString();

    private int Run(params string[] args)
    {
        using var input = new StringReader(string.Empty);
        return EnvKitCli.Run(
            new CliServices
            {
                Store = _store,
                Credentials = new InMemoryCredentialStore(),
                Vault = _vault,
                Profiles = new InMemoryProfileStore(),
                Processes = _processes,
                ProcessEnvironment = () => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Path"] = @"C:\Windows" },
                FileSystem = new FakeFileSystem().WithFiles(@"C:\Windows\node.exe"),
                Files = _files,
                Git = new NoGit(),
                Snapshots = _snapshots,
                Broadcaster = new CountingBroadcaster(),
                SystemFallback = _ => null,
                In = input,
                Out = _output,
                Error = _output,
                Prompt = _prompt,
                Elevation = new FakeElevation(_store) { IsElevated = true },
            },
            args);
    }

    [Fact]
    public void List_masks_secret_values_and_reports_problems()
    {
        _files.Seed(_env, Original + "PORT=4000\nnot an assignment\n");

        Assert.Equal(0, Run("dotenv", "list", "-f", _env));

        Assert.Contains("PORT = 4000", Output, StringComparison.Ordinal);
        Assert.Contains("API_KEY = ********", Output, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-live-123", Output, StringComparison.Ordinal);
        Assert.Contains("PORT is assigned more than once", Output, StringComparison.Ordinal);
        Assert.Contains("line 5 is not KEY=VALUE", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Set_changes_only_the_value_and_snapshots_the_file()
    {
        Assert.Equal(0, Run("dotenv", "set", "PORT", "8080", "-f", _env, "--yes"));

        Assert.Equal(Original.Replace("PORT=3000", "PORT=8080", StringComparison.Ordinal), _files.Text(_env));
        Assert.Contains("- PORT = 3000", Output, StringComparison.Ordinal);
        Assert.Contains("+ PORT = 8080", Output, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetBytes(Original), Assert.Single(Assert.Single(_snapshots.Saved).FileList).Content);
    }

    [Fact]
    public void Set_without_a_value_prompts_and_never_echoes_it()
    {
        _prompt.Secret = "sk-new";

        Run("dotenv", "set", "API_KEY", "-f", _env, "--yes");

        Assert.Contains("API_KEY=sk-new   # rotate monthly", _files.Text(_env), StringComparison.Ordinal);
        Assert.DoesNotContain("sk-new", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Unset_and_rename_keep_the_rest_of_the_file()
    {
        Run("dotenv", "rename", "PORT", "HTTP_PORT", "-f", _env, "--yes");
        Run("dotenv", "unset", "API_KEY", "-f", _env, "--yes");

        Assert.Equal("# local settings\nHTTP_PORT=3000\n", _files.Text(_env));
        Assert.Equal(1, Run("dotenv", "unset", "NOPE", "-f", _env, "--yes"));
    }

    [Fact]
    public void Link_with_move_puts_the_secret_in_the_vault_and_a_reference_in_the_file()
    {
        Assert.Equal(0, Run("dotenv", "link", "API_KEY", "openai", "--move", "-f", _env, "--yes"));

        Assert.Equal("sk-live-123", _vault.Get("openai"));
        Assert.Contains("API_KEY=${vault:openai}   # rotate monthly", _files.Text(_env), StringComparison.Ordinal);
        Assert.DoesNotContain("sk-live-123", _files.Text(_env), StringComparison.Ordinal);
    }

    [Fact]
    public void Declined_link_does_not_touch_the_vault()
    {
        _prompt.Answer = false;

        Assert.Equal(5, Run("dotenv", "link", "API_KEY", "openai", "--move", "-f", _env));

        Assert.Null(_vault.Get("openai"));
        Assert.Equal(Original, _files.Text(_env));
    }

    [Fact]
    public void Run_loads_env_files_and_fills_in_vault_references()
    {
        _vault.Set("openai", "sk-from-vault");
        _files.Seed(_env, "PORT=3000\nAPI_KEY=${vault:openai}\n");

        Run("run", "--env-file", _env, "--", "node", "server.js");

        Assert.Equal("3000", _processes.Environment["PORT"]);
        Assert.Equal("sk-from-vault", _processes.Environment["API_KEY"]);
        Assert.Equal(["server.js"], _processes.Arguments);
    }

    [Fact]
    public void Run_stops_when_an_env_file_needs_a_missing_secret()
    {
        _files.Seed(_env, "API_KEY=${vault:missing}\n");

        Assert.Equal(1, Run("run", "-e", _env, "--", "node"));
        Assert.Null(_processes.FileName);
    }

    [Fact]
    public void Scan_finds_files_and_user_secrets_projects()
    {
        File.WriteAllText(Path.Combine(_root, ".env"), "A=1");
        File.WriteAllText(Path.Combine(_root, "Api.csproj"), "<Project><PropertyGroup><UserSecretsId>scan-test</UserSecretsId></PropertyGroup></Project>");

        Assert.Equal(0, Run("dotenv", "scan", _root));

        Assert.Contains(".env", Output, StringComparison.Ordinal);
        Assert.Contains("user secrets for Api.csproj (no secrets yet)", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void User_secrets_are_set_for_the_project_in_a_folder()
    {
        File.WriteAllText(Path.Combine(_root, "Api.csproj"), "<Project><PropertyGroup><UserSecretsId>cli-test-id</UserSecretsId></PropertyGroup></Project>");

        Assert.Equal(0, Run("user-secrets", "set", "Stripe:ApiKey", "sk_test", "-p", _root, "--yes"));

        var path = ProjectScanner.UserSecretsPath("cli-test-id")!;
        Assert.Equal("sk_test", UserSecretsDocument.Parse(_files.Read(path)).Get("Stripe:ApiKey"));

        Run("user-secrets", "list", "-p", _root);
        Assert.Contains("Stripe:ApiKey = ********", Output, StringComparison.Ordinal);
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
