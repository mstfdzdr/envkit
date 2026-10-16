using EnvKit.Core.Credentials;
using EnvKit.Core.IO;
using EnvKit.Core.Plans;
using EnvKit.Core.Snapshots;
using EnvKit.Core.Variables;
using EnvKit.Windows.Credentials;
using EnvKit.Windows.Elevation;
using EnvKit.Windows.Security;
using EnvKit.Windows.Variables;

namespace EnvKit.App;

public sealed class AppServices
{
    public required IEnvStore Store { get; init; }

    public required ICredentialStore Credentials { get; init; }

    public required IFileSystem FileSystem { get; init; }

    public required ISnapshotStore Snapshots { get; init; }

    public required IEnvChangeBroadcaster Broadcaster { get; init; }

    /// <summary>Variables Windows supplies outside the registry; see <see cref="EnvLookup"/>.</summary>
    public required Func<string, string?> SystemFallback { get; init; }

    public required IElevation Elevation { get; init; }

    /// <summary><see langword="null"/> disables update checks.</summary>
    public IAppUpdater? Updater { get; init; }

    public static AppServices CreateDefault() => new()
    {
        Updater = new VelopackUpdater(),
        Store = new RegistryEnvStore(),
        Credentials = new WindowsCredentialStore(),
        FileSystem = PhysicalFileSystem.Instance,
        Snapshots = new FileSnapshotStore(FileSnapshotStore.DefaultDirectory, new DpapiProtector()),
        Broadcaster = new SettingChangeBroadcaster(),
        SystemFallback = Environment.GetEnvironmentVariable,
        Elevation = WindowsElevation.ForCurrentProcess(),
    };

    public EnvLookup Lookup() => new(Store, SystemFallback);

    public PlanBuilder NewPlan() => new(Store, Credentials);

    public PlanExecutor Executor() => new(Store, Snapshots, Broadcaster, Elevation, Credentials);
}
