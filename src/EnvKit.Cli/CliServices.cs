using EnvKit.Core.Credentials;
using EnvKit.Core.DotEnv;
using EnvKit.Core.IO;
using EnvKit.Core.Paths;
using EnvKit.Core.Plans;
using EnvKit.Core.Profiles;
using EnvKit.Core.Snapshots;
using EnvKit.Core.Variables;
using EnvKit.Core.Vault;
using EnvKit.Windows.Credentials;
using EnvKit.Windows.Elevation;
using EnvKit.Windows.Security;
using EnvKit.Windows.Variables;

namespace EnvKit.Cli;

public interface IPrompt
{
    /// <summary><see langword="false"/> when input is redirected (scripts, CI).</summary>
    bool IsInteractive { get; }

    bool Confirm(string question);

    /// <summary>Reads a secret without echoing it; <see langword="null"/> if input ended.</summary>
    string? ReadSecret(string label);
}

internal sealed class ConsolePrompt : IPrompt
{
    public bool IsInteractive => !Console.IsInputRedirected;

    public bool Confirm(string question)
    {
        Console.Write($"{question} [y/N] ");
        var answer = Console.ReadLine()?.Trim();
        return string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
    }

    public string? ReadSecret(string label)
    {
        Console.Write($"{label}: ");
        var secret = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return secret.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (secret.Length > 0)
                {
                    secret.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                secret.Append(key.KeyChar);
            }
        }
    }
}

public sealed class CliServices
{
    public required IEnvStore Store { get; init; }

    public required ICredentialStore Credentials { get; init; }

    public required IVault Vault { get; init; }

    public required IProfileStore Profiles { get; init; }

    public required IProcessRunner Processes { get; init; }

    /// <summary>The environment <c>envkit run</c> starts from: this process's.</summary>
    public required Func<IReadOnlyDictionary<string, string>> ProcessEnvironment { get; init; }

    public required IFileSystem FileSystem { get; init; }

    /// <summary>Reads and atomically writes .env and secrets.json files.</summary>
    public required IFileStore Files { get; init; }

    public required IGitInspector Git { get; init; }

    public required ISnapshotStore Snapshots { get; init; }

    public required IEnvChangeBroadcaster Broadcaster { get; init; }

    /// <summary>Variables Windows supplies outside the registry; see <see cref="EnvLookup"/>.</summary>
    public required Func<string, string?> SystemFallback { get; init; }

    /// <summary>Standard input, for <c>--password-stdin</c>.</summary>
    public required TextReader In { get; init; }

    public required TextWriter Out { get; init; }

    public required TextWriter Error { get; init; }

    public required IPrompt Prompt { get; init; }

    public required IElevation Elevation { get; init; }

    public static CliServices CreateDefault() => new()
    {
        Store = new RegistryEnvStore(),
        Credentials = new WindowsCredentialStore(),
        Vault = new FileVault(FileVault.DefaultPath, new DpapiProtector()),
        Profiles = new FileProfileStore(FileProfileStore.DefaultDirectory),
        Processes = new ProcessRunner(),
        ProcessEnvironment = ProcessRunner.CurrentEnvironment,
        FileSystem = PhysicalFileSystem.Instance,
        Files = PhysicalFileStore.Instance,
        Git = new GitInspector(),
        Snapshots = new FileSnapshotStore(FileSnapshotStore.DefaultDirectory, new DpapiProtector()),
        Broadcaster = new SettingChangeBroadcaster(),
        SystemFallback = Environment.GetEnvironmentVariable,
        In = Console.In,
        Out = Console.Out,
        Error = Console.Error,
        Prompt = new ConsolePrompt(),
        Elevation = WindowsElevation.ForCurrentProcess(),
    };

    public EnvLookup Lookup() => new(Store, SystemFallback);

    public PathReport AnalyzePath() => new PathAnalyzer(FileSystem).Analyze(Store, Lookup());

    public PlanBuilder NewPlan() => new(Store, Credentials, Files);

    public PlanExecutor Executor() => new(Store, Snapshots, Broadcaster, Elevation, Credentials, Files);
}
