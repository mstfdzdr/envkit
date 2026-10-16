using EnvKit.Core.Snapshots;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Plans;

public interface IElevation
{
    /// <summary>Whether this process can write machine-scope variables itself.</summary>
    bool IsElevated { get; }

    /// <summary>Applies a machine-only plan in an elevated helper process (one UAC prompt).</summary>
    /// <exception cref="OperationCanceledException">The user declined the prompt; nothing was written.</exception>
    /// <exception cref="PlanConflictException">The machine variables changed before the helper ran.</exception>
    /// <exception cref="ElevatedApplyException">The helper failed; it reverts its own partial writes.</exception>
    void ApplyElevated(ChangePlan machinePlan);
}

/// <summary>
/// Applies a plan, elevating only for its machine-scope part when this process cannot.
/// User changes are written here rather than by the helper: if the prompt is answered with
/// another administrator's credentials, the helper's HKCU belongs to that administrator.
/// </summary>
public sealed class PlanExecutor(
    IEnvStore store,
    ISnapshotStore snapshots,
    IEnvChangeBroadcaster broadcaster,
    IElevation elevation,
    TimeProvider? timeProvider = null)
{
    public PlanResult Execute(ChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.IsEmpty || !plan.RequiresElevation || elevation.IsElevated)
        {
            return new PlanApplier(store, snapshots, broadcaster, timeProvider).Apply(plan);
        }

        var local = new PlanApplier(store, snapshots: null, broadcaster: null, timeProvider);
        var changes = PlanApplier.VariableChanges(plan);

        // Fail before showing a UAC prompt for a plan that cannot apply anyway.
        local.EnsureCurrent(changes);

        // Taken here, not by the helper, so it is protected with this user's DPAPI key.
        var snapshot = Snapshot.Capture(store, $"Before: {plan.Description}", plan.Id, timeProvider);
        snapshots.Save(snapshot);

        // User changes first: they can be reverted without elevation if the prompt is declined.
        var userChanges = changes.Where(c => !c.RequiresElevation).ToList();
        var machinePlan = plan with { Changes = [.. changes.Where(c => c.RequiresElevation)] };
        local.WriteAll(userChanges, snapshot.Info);

        try
        {
            elevation.ApplyElevated(machinePlan);
        }
        catch (Exception failure)
        {
            try
            {
                local.RevertAll(userChanges);
            }
            catch (AggregateException revertFailure)
            {
                throw new PlanApplyException(snapshot.Info, new AggregateException(failure, revertFailure));
            }

            throw;
        }

        return new PlanResult(snapshot.Info, broadcaster.Broadcast());
    }
}

/// <summary>The elevated helper could not apply its plan; it reverted what it had written.</summary>
public sealed class ElevatedApplyException : Exception
{
    public ElevatedApplyException()
    {
    }

    public ElevatedApplyException(string message)
        : base(message)
    {
    }

    public ElevatedApplyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
