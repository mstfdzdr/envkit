using EnvKit.Core.Plans;
using EnvKit.Testing;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Tests.Plans;

public class PlanApplierTests
{
    private readonly InMemoryEnvStore _store = new();
    private readonly InMemorySnapshotStore _snapshots = new();
    private readonly CountingBroadcaster _broadcaster = new();

    private PlanApplier Applier(IEnvStore? store = null) => new(store ?? _store, _snapshots, _broadcaster);

    [Fact]
    public void Applies_changes_after_snapshotting_and_broadcasts_once()
    {
        _store.Set(new EnvVariable("X", "old", EnvScope.User));
        var plan = new PlanBuilder(_store)
            .Set("X", EnvScope.User, "new")
            .Set("Y", EnvScope.User, "created")
            .Build("edit");

        var result = Applier().Apply(plan);

        Assert.Equal("new", _store.Get("X", EnvScope.User)?.Value);
        Assert.Equal("created", _store.Get("Y", EnvScope.User)?.Value);
        var snapshot = Assert.Single(_snapshots.Saved);
        Assert.Equal(result.Snapshot, snapshot.Info);
        Assert.Equal(plan.Id, snapshot.Info.PlanId);
        Assert.Contains(snapshot.Variables, v => v is { Name: "X", Value: "old" });
        Assert.DoesNotContain(snapshot.Variables, v => v.Name == "Y");
        Assert.Equal(1, _broadcaster.Count);
    }

    [Fact]
    public void Empty_plan_does_nothing()
    {
        var result = Applier().Apply(new PlanBuilder(_store).Build("noop"));

        Assert.Null(result.Snapshot);
        Assert.Empty(_snapshots.Saved);
        Assert.Equal(0, _broadcaster.Count);
    }

    [Fact]
    public void Refuses_a_plan_whose_before_state_is_stale()
    {
        _store.Set(new EnvVariable("Path", @"C:\a", EnvScope.User));
        var plan = new PlanBuilder(_store).Set("Path", EnvScope.User, @"C:\a;C:\b").Build("add");

        // An installer touches PATH between preview and apply.
        _store.Set(new EnvVariable("Path", @"C:\a;C:\installer", EnvScope.User));

        var conflict = Assert.Throws<PlanConflictException>(() => Applier().Apply(plan));
        Assert.Equal(@"User\Path", Assert.Single(conflict.Conflicts).Subject);
        Assert.Equal(@"C:\a;C:\installer", _store.Get("Path", EnvScope.User)?.Value);
        Assert.Empty(_snapshots.Saved);
    }

    [Fact]
    public void Failed_write_reverts_earlier_writes_and_keeps_the_original_exception()
    {
        _store.Set(new EnvVariable("U", "before", EnvScope.User));
        var plan = new PlanBuilder(_store)
            .Set("U", EnvScope.User, "after")
            .Set("NEW_USER", EnvScope.User, "x")
            .Set("M", EnvScope.Machine, "x")
            .Build("mixed");

        Assert.Throws<ElevationRequiredException>(() => Applier(new MachineReadOnlyStore(_store)).Apply(plan));

        Assert.Equal("before", _store.Get("U", EnvScope.User)?.Value);
        Assert.Null(_store.Get("NEW_USER", EnvScope.User));
        Assert.Equal(0, _broadcaster.Count);
    }

    [Fact]
    public void Restoring_the_snapshot_undoes_the_plan()
    {
        _store.Set(new EnvVariable("Path", @"C:\a;;C:\a", EnvScope.User, EnvValueKind.Expandable));
        var original = _store.GetAll(EnvScope.User);
        var result = Applier().Apply(new PlanBuilder(_store).Set("Path", EnvScope.User, @"C:\a").Set("Z", EnvScope.User, "1").Build("clean"));

        var undo = new PlanBuilder(_store).Restore(_snapshots.Load(result.Snapshot!.Id)).Build("undo");
        Applier().Apply(undo);

        Assert.Equal(original, _store.GetAll(EnvScope.User));
    }
}
