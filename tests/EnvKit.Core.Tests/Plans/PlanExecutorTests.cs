using EnvKit.Core.Plans;
using EnvKit.Core.Variables;
using EnvKit.Testing;

namespace EnvKit.Core.Tests.Plans;

public class PlanExecutorTests
{
    private readonly InMemoryEnvStore _store = new();
    private readonly InMemorySnapshotStore _snapshots = new();
    private readonly CountingBroadcaster _broadcaster = new();
    private readonly FakeElevation _elevation;

    public PlanExecutorTests()
    {
        _store.Set(new EnvVariable("U", "user-before", EnvScope.User));
        _store.Set(new EnvVariable("M", "machine-before", EnvScope.Machine));
        _elevation = new FakeElevation(_store);
    }

    /// <summary>As seen from an unelevated process.</summary>
    private PlanExecutor Executor() =>
        new(new MachineReadOnlyStore(_store), _snapshots, _broadcaster, _elevation);

    private ChangePlan MixedPlan() => new PlanBuilder(_store)
        .Set("U", EnvScope.User, "user-after")
        .Set("M", EnvScope.Machine, "machine-after")
        .Build("mixed");

    [Fact]
    public void User_only_plan_never_elevates()
    {
        Executor().Execute(new PlanBuilder(_store).Set("U", EnvScope.User, "x").Build("user"));

        Assert.Empty(_elevation.Requests);
        Assert.Equal("x", _store.Get("U", EnvScope.User)?.Value);
        Assert.Single(_snapshots.Saved);
    }

    [Fact]
    public void Already_elevated_process_applies_directly()
    {
        _elevation.IsElevated = true;

        new PlanExecutor(_store, _snapshots, _broadcaster, _elevation).Execute(MixedPlan());

        Assert.Empty(_elevation.Requests);
        Assert.Equal("machine-after", _store.Get("M", EnvScope.Machine)?.Value);
    }

    [Fact]
    public void Mixed_plan_writes_user_changes_here_and_only_machine_changes_elevated()
    {
        var result = Executor().Execute(MixedPlan());

        var request = Assert.Single(_elevation.Requests);
        Assert.All(request.Changes.Cast<VariableChange>(), c => Assert.Equal(EnvScope.Machine, c.Scope));
        Assert.Equal("user-after", _store.Get("U", EnvScope.User)?.Value);
        Assert.Equal("machine-after", _store.Get("M", EnvScope.Machine)?.Value);

        var snapshot = Assert.Single(_snapshots.Saved);
        Assert.Equal(result.Snapshot, snapshot.Info);
        Assert.Contains(snapshot.Variables, v => v is { Name: "M", Value: "machine-before" });
        Assert.Equal(1, _broadcaster.Count);
    }

    [Fact]
    public void Declined_prompt_reverts_user_changes()
    {
        _elevation.Failure = new OperationCanceledException("declined");

        Assert.Throws<OperationCanceledException>(() => Executor().Execute(MixedPlan()));

        Assert.Equal("user-before", _store.Get("U", EnvScope.User)?.Value);
        Assert.Equal("machine-before", _store.Get("M", EnvScope.Machine)?.Value);
        Assert.Equal(0, _broadcaster.Count);
    }

    [Fact]
    public void Stale_plan_fails_before_prompting()
    {
        var plan = MixedPlan();
        _store.Set(new EnvVariable("M", "changed by an installer", EnvScope.Machine));

        Assert.Throws<PlanConflictException>(() => Executor().Execute(plan));

        Assert.Empty(_elevation.Requests);
        Assert.Empty(_snapshots.Saved);
        Assert.Equal("user-before", _store.Get("U", EnvScope.User)?.Value);
    }
}
