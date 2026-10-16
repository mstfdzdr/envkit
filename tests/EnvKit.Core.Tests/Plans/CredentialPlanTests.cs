using System.Text;
using EnvKit.Core.Credentials;
using EnvKit.Core.Plans;
using EnvKit.Core.Snapshots;
using EnvKit.Core.Variables;
using EnvKit.Testing;

namespace EnvKit.Core.Tests.Plans;

public sealed class CredentialPlanTests : IDisposable
{
    private static readonly CredentialKey Git = new("git:https://github.com", CredentialKind.Generic);
    private static readonly CredentialKey Docker = new("https://index.docker.io/v1/docker", CredentialKind.Generic);
    private static readonly CredentialKey Share = new("fileserver", CredentialKind.DomainPassword);

    private readonly InMemoryEnvStore _env = new();
    private readonly InMemoryCredentialStore _credentials = new();
    private readonly InMemorySnapshotStore _snapshots = new();
    private readonly CountingBroadcaster _broadcaster = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "EnvKit.Tests", Guid.NewGuid().ToString("N"));

    public CredentialPlanTests()
    {
        _credentials.Seed(Git, State("old-token"));
        _credentials.Seed(Docker, State("docker-pass"));
        _credentials.Seed(Share, new CredentialState(@"DOMAIN\me", null, CredentialPersistence.Enterprise));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static CredentialState State(string secret, string user = "me") =>
        new(user, Encoding.Unicode.GetBytes(secret), CredentialPersistence.LocalMachine);

    private PlanBuilder Builder() => new(_env, _credentials);

    private PlanResult Apply(ChangePlan plan) => new PlanApplier(_env, _snapshots, _broadcaster, _credentials).Apply(plan);

    [Fact]
    public void Edit_is_snapshotted_and_restorable()
    {
        var result = Apply(Builder().SetCredential(Git, State("new-token")).Build("rotate"));

        Assert.Equal("new-token", _credentials.Get(Git)!.State.SecretText);
        Assert.Equal(0, _broadcaster.Count);
        var saved = Assert.Single(_snapshots.Saved);
        Assert.Equal("old-token", Assert.Single(saved.CredentialList).State!.SecretText);

        Apply(Builder().Restore(_snapshots.Load(result.Snapshot!.Id)).Build("undo"));

        Assert.Equal("old-token", _credentials.Get(Git)!.State.SecretText);
    }

    [Fact]
    public void Creating_then_restoring_deletes_again()
    {
        var key = new CredentialKey("my-app/token", CredentialKind.Generic);
        var plan = Builder().SetCredential(key, State("x")).Build("create");
        Assert.Null(Assert.IsType<CredentialChange>(Assert.Single(plan.Changes)).Before);

        var result = Apply(plan);
        Apply(Builder().Restore(_snapshots.Load(result.Snapshot!.Id)).Build("undo"));

        Assert.Null(_credentials.Get(key));
    }

    [Fact]
    public void Bulk_delete_is_restorable_except_for_domain_credentials()
    {
        var plan = Builder().DeleteCredential(Git).DeleteCredential(Docker).DeleteCredential(Share).Build("cleanup");
        Assert.Equal([false, false, true], plan.Changes.Cast<CredentialChange>().Select(c => c.IsIrreversible));

        var result = Apply(plan);
        Assert.Empty(_credentials.List());

        var undo = Builder().Restore(_snapshots.Load(result.Snapshot!.Id)).Build("undo");
        Apply(undo);

        Assert.Equal("old-token", _credentials.Get(Git)?.State.SecretText);
        Assert.Equal("docker-pass", _credentials.Get(Docker)?.State.SecretText);
        Assert.Null(_credentials.Get(Share));
    }

    [Fact]
    public void Stale_credential_is_a_conflict_and_nothing_is_written()
    {
        var plan = Builder().SetCredential(Git, State("mine")).DeleteCredential(Docker).Build("t");
        _credentials.Write(Git, State("rotated by git"));

        var conflict = Assert.Throws<PlanConflictException>(() => Apply(plan));

        Assert.Equal("Credential git:https://github.com", Assert.Single(conflict.Conflicts).Subject);
        Assert.NotNull(_credentials.Get(Docker));
        Assert.Empty(_snapshots.Saved);
    }

    [Fact]
    public void Failed_write_reverts_earlier_credential_changes()
    {
        var plan = Builder().DeleteCredential(Docker).SetCredential(Git, State("new")).Build("t");
        _credentials.FailOnWrite.Add(Git);

        Assert.Throws<InvalidOperationException>(() => Apply(plan));

        Assert.Equal("docker-pass", _credentials.Get(Docker)?.State.SecretText);
        Assert.Equal("old-token", _credentials.Get(Git)?.State.SecretText);
    }

    [Fact]
    public void Repeated_edits_collapse_and_setting_the_same_state_is_no_change()
    {
        Assert.True(Builder().SetCredential(Git, State("old-token")).Build("t").IsEmpty);

        var change = Assert.IsType<CredentialChange>(Assert.Single(
            Builder().SetCredential(Git, State("a")).SetCredential(Git, State("b")).Build("t").Changes));
        Assert.Equal("old-token", change.Before!.SecretText);
        Assert.Equal("b", change.After!.SecretText);
    }

    [Fact]
    public void Only_generic_credentials_can_be_set()
    {
        Assert.Throws<NotSupportedException>(() => Builder().SetCredential(Share, State("x")));
        Assert.Throws<ArgumentException>(() =>
            Builder().SetCredential(Git, new CredentialState("me", new byte[CredentialState.MaxSecretBytes + 1], CredentialPersistence.LocalMachine)));
    }

    [Fact]
    public void Summary_describes_changes_without_secrets()
    {
        var plan = Builder()
            .SetCredential(Git, State("hunter2", "you"))
            .DeleteCredential(Share)
            .Build("t");

        var summaries = PlanSummary.Summarize(plan);
        var text = string.Join("\n", summaries.SelectMany(s => s.Lines.Select(l => l.Text).Prepend(s.Note).Prepend(s.Title)));

        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("old-token", text, StringComparison.Ordinal);
        Assert.Contains("password: changed", text, StringComparison.Ordinal);
        Assert.Contains("user: you", text, StringComparison.Ordinal);
        Assert.Contains("cannot be restored", summaries[1].Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Executor_applies_credentials_locally_and_elevates_only_machine_variables()
    {
        _env.Set(new EnvVariable("M", "before", EnvScope.Machine));
        var elevation = new FakeElevation(_env);
        var plan = Builder().SetCredential(Git, State("new")).Set("M", EnvScope.Machine, "after").Build("mixed");

        new PlanExecutor(new MachineReadOnlyStore(_env), _snapshots, _broadcaster, elevation, _credentials).Execute(plan);

        var request = Assert.Single(elevation.Requests);
        Assert.All(request.Changes, c => Assert.IsType<VariableChange>(c));
        Assert.Equal("new", _credentials.Get(Git)!.State.SecretText);
        Assert.Single(Assert.Single(_snapshots.Saved).CredentialList);
    }

    [Fact]
    public void Snapshot_files_keep_credentials_and_read_old_snapshots_without_them()
    {
        var store = new FileSnapshotStore(_directory, new XorProtector());
        var plan = Builder().DeleteCredential(Git).Build("t");
        var snapshot = Snapshot.BeforePlan(_env, plan);

        store.Save(snapshot);
        store.Save(Snapshot.Capture(_env, "variables only"));
        var loaded = store.Load(snapshot.Id);

        Assert.Equal(_credentials.Get(Git)!.State, Assert.Single(loaded.CredentialList).State);
        Assert.Empty(store.Load(store.List().Single(i => i.Reason == "variables only").Id).CredentialList);
    }
}
