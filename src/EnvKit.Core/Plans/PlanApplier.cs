using EnvKit.Core.Credentials;
using EnvKit.Core.Snapshots;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Plans;

/// <param name="Snapshot">Taken before the first write; <see langword="null"/> for an empty plan or when not snapshotting.</param>
/// <param name="Broadcast">Whether running programs were notified.</param>
public sealed record PlanResult(SnapshotInfo? Snapshot, bool Broadcast);

/// <param name="snapshots"><see langword="null"/> to skip the snapshot (the elevated helper: its caller already took one).</param>
/// <param name="broadcaster"><see langword="null"/> to leave notifying to the caller.</param>
/// <param name="credentials">Required only for plans with credential changes.</param>
public sealed class PlanApplier(
    IEnvStore store,
    ISnapshotStore? snapshots,
    IEnvChangeBroadcaster? broadcaster,
    ICredentialStore? credentials = null,
    TimeProvider? timeProvider = null)
{
    /// <summary>
    /// Checks every precondition, snapshots the current state, writes the changes and
    /// broadcasts once. If a write fails, the changes already written are reverted and the
    /// original exception (e.g. <see cref="ElevationRequiredException"/>) propagates.
    /// </summary>
    /// <exception cref="PlanConflictException">The stores no longer match the plan's before state.</exception>
    /// <exception cref="PlanApplyException">A write failed and reverting the earlier writes also failed.</exception>
    public PlanResult Apply(ChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.IsEmpty)
        {
            return new PlanResult(null, Broadcast: false);
        }

        EnsureCurrent(plan.Changes);

        SnapshotInfo? snapshot = null;
        if (snapshots is not null)
        {
            var captured = Snapshot.BeforePlan(store, plan, timeProvider);
            snapshots.Save(captured);
            snapshot = captured.Info;
        }

        WriteAll(plan.Changes, snapshot);
        return new PlanResult(snapshot, plan.Changes.Any(c => c is VariableChange) && (broadcaster?.Broadcast() ?? false));
    }

    /// <exception cref="PlanConflictException">Some changes no longer match the stores.</exception>
    public void EnsureCurrent(IEnumerable<PlanChange> changes)
    {
        var conflicts = changes.Where(c => !IsCurrent(c)).ToList();
        if (conflicts.Count > 0)
        {
            throw new PlanConflictException(conflicts);
        }
    }

    /// <summary>Writes in order; if one write fails, the earlier ones are reverted before rethrowing.</summary>
    public void WriteAll(IReadOnlyList<PlanChange> changes, SnapshotInfo? snapshot)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var applied = new List<PlanChange>();
        try
        {
            foreach (var change in changes)
            {
                Write(change, forward: true);
                applied.Add(change);
            }
        }
        catch (Exception failure)
        {
            var rollbackFailures = TryRevert(applied);
            if (rollbackFailures.Count > 0)
            {
                throw new PlanApplyException(snapshot, new AggregateException([failure, .. rollbackFailures]));
            }

            throw;
        }
    }

    /// <summary>Puts every change's before state back, newest first.</summary>
    /// <exception cref="AggregateException">Some changes could not be reverted.</exception>
    public void RevertAll(IReadOnlyList<PlanChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var failures = TryRevert(changes);
        if (failures.Count > 0)
        {
            throw new AggregateException(failures);
        }
    }

    private bool IsCurrent(PlanChange change) => change switch
    {
        VariableChange v => (store.Get(v.Name, v.Scope) is { } current ? new VariableValue(current.Value, current.Kind) : null) == v.Before,
        CredentialChange c => Credentials.Get(c.Key)?.State == c.Before,
        _ => throw new NotSupportedException(change.GetType().Name),
    };

    private void Write(PlanChange change, bool forward)
    {
        switch (change)
        {
            case VariableChange v:
                var value = forward ? v.After : v.Before;
                if (value is null)
                {
                    store.Delete(v.Name, v.Scope);
                }
                else
                {
                    store.Set(new EnvVariable(v.Name, value.Value, v.Scope, value.Kind));
                }

                break;

            case CredentialChange c:
                var state = forward ? c.After : c.Before;
                if (state is null)
                {
                    Credentials.Delete(c.Key);
                }
                else if (state.Secret is not null)
                {
                    Credentials.Write(c.Key, state);
                }
                else
                {
                    // Only reachable when reverting the deletion of a domain credential.
                    throw new NotSupportedException($"{c.Key} cannot be recreated: Windows never revealed its secret.");
                }

                break;

            default:
                throw new NotSupportedException(change.GetType().Name);
        }
    }

    private ICredentialStore Credentials =>
        credentials ?? throw new InvalidOperationException("This plan changes credentials, but no credential store was given.");

    private List<Exception> TryRevert(IReadOnlyList<PlanChange> applied)
    {
        var failures = new List<Exception>();
        for (var i = applied.Count - 1; i >= 0; i--)
        {
            try
            {
                Write(applied[i], forward: false);
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
    public PlanConflictException(IReadOnlyList<PlanChange> conflicts)
        : base($"Changed since the plan was made: {string.Join(", ", conflicts.Select(c => c.Subject))}.") =>
        Conflicts = conflicts;

    public PlanConflictException()
        : this([])
    {
    }

    public PlanConflictException(string message)
        : base(message) => Conflicts = [];

    public PlanConflictException(string message, Exception innerException)
        : base(message, innerException) => Conflicts = [];

    public IReadOnlyList<PlanChange> Conflicts { get; }
}

/// <summary>The plan is partially applied; <see cref="Snapshot"/>, when present, restores the state before it.</summary>
public sealed class PlanApplyException : Exception
{
    public PlanApplyException(SnapshotInfo? snapshot, Exception innerException)
        : base(
            snapshot is null
                ? "The plan was partially applied and could not be reverted."
                : $"The plan was partially applied and could not be reverted. Restore snapshot {snapshot.Id}.",
            innerException) =>
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
