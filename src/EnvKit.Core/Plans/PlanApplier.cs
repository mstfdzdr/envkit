using EnvKit.Core.Snapshots;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Plans;

/// <param name="Snapshot">Taken before the first write; <see langword="null"/> for an empty plan.</param>
/// <param name="Broadcast">Whether running programs were notified.</param>
public sealed record PlanResult(SnapshotInfo? Snapshot, bool Broadcast);

public sealed class PlanApplier(IEnvStore store, ISnapshotStore snapshots, IEnvChangeBroadcaster broadcaster, TimeProvider? timeProvider = null)
{
    /// <summary>
    /// Checks every precondition, snapshots the current state, writes the changes and
    /// broadcasts once. If a write fails, the changes already written are reverted and the
    /// original exception (e.g. <see cref="ElevationRequiredException"/>) propagates.
    /// </summary>
    /// <exception cref="PlanConflictException">The store no longer matches the plan's before state.</exception>
    /// <exception cref="PlanApplyException">A write failed and reverting the earlier writes also failed.</exception>
    public PlanResult Apply(ChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.IsEmpty)
        {
            return new PlanResult(null, Broadcast: false);
        }

        var changes = plan.Changes.Cast<VariableChange>().ToList();
        var conflicts = changes.Where(c => Current(c) != c.Before).ToList();
        if (conflicts.Count > 0)
        {
            throw new PlanConflictException(conflicts);
        }

        var snapshot = Snapshot.Capture(store, $"Before: {plan.Description}", plan.Id, timeProvider);
        snapshots.Save(snapshot);

        var applied = new List<VariableChange>();
        try
        {
            foreach (var change in changes)
            {
                Write(change.Scope, change.Name, change.After);
                applied.Add(change);
            }
        }
        catch (Exception failure)
        {
            var rollbackFailures = Revert(applied);
            if (rollbackFailures.Count > 0)
            {
                throw new PlanApplyException(snapshot.Info, new AggregateException([failure, .. rollbackFailures]));
            }

            throw;
        }

        return new PlanResult(snapshot.Info, broadcaster.Broadcast());
    }

    private VariableValue? Current(VariableChange change) =>
        store.Get(change.Name, change.Scope) is { } v ? new VariableValue(v.Value, v.Kind) : null;

    private void Write(EnvScope scope, string name, VariableValue? value)
    {
        if (value is null)
        {
            store.Delete(name, scope);
        }
        else
        {
            store.Set(new EnvVariable(name, value.Value, scope, value.Kind));
        }
    }

    private List<Exception> Revert(List<VariableChange> applied)
    {
        var failures = new List<Exception>();
        for (var i = applied.Count - 1; i >= 0; i--)
        {
            try
            {
                Write(applied[i].Scope, applied[i].Name, applied[i].Before);
            }
#pragma warning disable CA1031 // Keep reverting the rest; every failure is reported to the caller.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failures.Add(ex);
            }
        }

        return failures;
    }
}

public sealed class PlanConflictException : Exception
{
    public PlanConflictException(IReadOnlyList<VariableChange> conflicts)
        : base($"Changed since the plan was made: {string.Join(", ", conflicts.Select(c => $@"{c.Scope}\{c.Name}"))}.") =>
        Conflicts = conflicts;

    public PlanConflictException()
        : this([])
    {
    }

    public PlanConflictException(string message)
        : base(message) => Conflicts = [];

    public PlanConflictException(string message, Exception innerException)
        : base(message, innerException) => Conflicts = [];

    public IReadOnlyList<VariableChange> Conflicts { get; }
}

/// <summary>The plan is partially applied; <see cref="Snapshot"/> restores the state before it.</summary>
public sealed class PlanApplyException : Exception
{
    public PlanApplyException(SnapshotInfo snapshot, Exception innerException)
        : base($"The plan was partially applied and could not be reverted. Restore snapshot {snapshot.Id}.", innerException) =>
        Snapshot = snapshot;

    public PlanApplyException()
    {
    }

    public PlanApplyException(string message)
        : base(message)
    {
    }

    public PlanApplyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SnapshotInfo? Snapshot { get; }
}
