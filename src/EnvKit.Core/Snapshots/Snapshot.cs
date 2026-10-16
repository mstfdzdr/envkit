using System.Globalization;
using System.Text.Json.Serialization;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Snapshots;

public sealed record SnapshotInfo(string Id, DateTimeOffset CreatedAt, string Reason, Guid? PlanId);

public sealed record SnapshotVariable(EnvScope Scope, string Name, string Value, EnvValueKind Kind);

/// <summary>Every persistent variable in both scopes at one point in time.</summary>
public sealed record Snapshot(SnapshotInfo Info, IReadOnlyList<SnapshotVariable> Variables)
{
    [JsonIgnore]
    public string Id => Info.Id;

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
