using EnvKit.Core.IO;
using EnvKit.Core.Paths;
using EnvKit.Core.Plans;
using EnvKit.Core.Snapshots;
using EnvKit.Core.Variables;
using EnvKit.Windows.Elevation;
using EnvKit.Windows.Security;
using EnvKit.Windows.Variables;

namespace EnvKit.Cli;

public interface IPrompt
{
    /// <summary><see langword="false"/> when input is redirected (scripts, CI).</summary>
    bool IsInteractive { get; }

    bool Confirm(string question);
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
}

public sealed class CliServices
{
    public required IEnvStore Store { get; init; }

    public required IFileSystem FileSystem { get; init; }

    public required ISnapshotStore Snapshots { get; init; }

    public required IEnvChangeBroadcaster Broadcaster { get; init; }

    /// <summary>Variables Windows supplies outside the registry; see <see cref="EnvLookup"/>.</summary>
    public required Func<string, string?> SystemFallback { get; init; }

    public required TextWriter Out { get; init; }

    public required TextWriter Error { get; init; }

    public required IPrompt Prompt { get; init; }

    public required IElevation Elevation { get; init; }

    public static CliServices CreateDefault() => new()
    {
        Store = new RegistryEnvStore(),
        FileSystem = PhysicalFileSystem.Instance,
        Snapshots = new FileSnapshotStore(FileSnapshotStore.DefaultDirectory, new DpapiProtector()),
        Broadcaster = new SettingChangeBroadcaster(),
        SystemFallback = Environment.GetEnvironmentVariable,
        Out = Console.Out,
        Error = Console.Error,
        Prompt = new ConsolePrompt(),
        Elevation = WindowsElevation.ForCurrentProcess(),
    };

    public EnvLookup Lookup() => new(Store, SystemFallback);

    public PathReport AnalyzePath() => new PathAnalyzer(FileSystem).Analyze(Store, Lookup());

    public PlanBuilder NewPlan() => new(Store);

    public PlanExecutor Executor() => new(Store, Snapshots, Broadcaster, Elevation);
}
