using System.Globalization;
using System.Text.Json.Serialization;
using EnvKit.Core.Credentials;
using EnvKit.Core.Plans;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Snapshots;

public sealed record SnapshotInfo(string Id, DateTimeOffset CreatedAt, string Reason, Guid? PlanId);

public sealed record SnapshotVariable(EnvScope Scope, string Name, string Value, EnvValueKind Kind);

/// <param name="State"><see langword="null"/>: the credential did not exist.</param>
public sealed record SnapshotCredential(CredentialKey Key, CredentialState? State);

/// <param name="Content"><see langword="null"/>: the file did not exist.</param>
public sealed record SnapshotFile(string Path, FileFormat Format, byte[]? Content)
{
    public bool Equals(SnapshotFile? other) =>
        other is not null && Path == other.Path && Format == other.Format && FileChange.SameBytes(Content, other.Content);

    public override int GetHashCode() => HashCode.Combine(Path, Format, Content?.Length);
}

/// <summary>
/// Every persistent variable in both scopes at one point in time, plus the prior state of the
/// credentials the plan touched. Credentials are not copied wholesale: the snapshot should hold
/// no more secrets than the change needs to be undone.
/// </summary>
/// <param name="Credentials"><see langword="null"/> in snapshots written before credentials existed.</param>
/// <param name="Files">Prior content of the files a plan changed; <see langword="null"/> when none.</param>
public sealed record Snapshot(
    SnapshotInfo Info,
    IReadOnlyList<SnapshotVariable> Variables,
    IReadOnlyList<SnapshotCredential>? Credentials = null,
    IReadOnlyList<SnapshotFile>? Files = null)
{
    [JsonIgnore]
    public string Id => Info.Id;

    [JsonIgnore]
    public IReadOnlyList<SnapshotCredential> CredentialList => Credentials ?? [];

    [JsonIgnore]
    public IReadOnlyList<SnapshotFile> FileList => Files ?? [];

    /// <summary>The state before <paramref name="plan"/>: all variables and the credentials it changes.</summary>
    public static Snapshot BeforePlan(IEnvStore store, ChangePlan plan, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var credentials = plan.Changes
            .OfType<CredentialChange>()
            .Select(c => new SnapshotCredential(c.Key, c.Before))
            .ToList();
        var files = plan.Changes
            .OfType<FileChange>()
            .Select(f => new SnapshotFile(f.Path, f.Format, f.Before))
            .ToList();
        return Capture(store, $"Before: {plan.Description}", plan.Id, timeProvider) with
        {
            Credentials = credentials.Count == 0 ? null : credentials,
            Files = files.Count == 0 ? null : files,
        };
    }

    public static Snapshot Capture(IEnvStore store, string reason, Guid? planId = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();

        // Name order is age order down to 100 ns (retention relies on it); the suffix only
        // keeps ids unique if two snapshots ever share a tick.
        var id = string.Create(
            CultureInfo.InvariantCulture, $"{now:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid().ToString("N")[..6]}");

        var variables = new[] { EnvScope.Machine, EnvScope.User }
            .SelectMany(store.GetAll)
            .Select(v => new SnapshotVariable(v.Scope, v.Name, v.Value, v.Kind))
            .ToList();

        return new Snapshot(new SnapshotInfo(id, now, reason, planId), variables);
    }
}

public interface ISnapshotStore
{
    void Save(Snapshot snapshot);

    /// <summary>Newest first. Unreadable snapshots are skipped.</summary>
    IReadOnlyList<SnapshotInfo> List();

    /// <exception cref="KeyNotFoundException">No snapshot with that id.</exception>
    Snapshot Load(string id);
}
