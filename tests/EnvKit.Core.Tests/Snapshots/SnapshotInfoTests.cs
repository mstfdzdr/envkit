using System.Globalization;
using EnvKit.Core.Plans;
using EnvKit.Core.Snapshots;
using EnvKit.Testing;

namespace EnvKit.Core.Tests.Snapshots;

public sealed class SnapshotInfoTests
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr");

    [Fact]
    public void Plan_snapshots_store_only_the_description()
    {
        var env = new InMemoryEnvStore();
        var plan = new PlanBuilder(env).Build("Profil uygulandı");

        var info = Snapshot.BeforePlan(env, plan).Info;

        Assert.Equal("Profil uygulandı", info.Reason);
        Assert.Equal("Before: Profil uygulandı", info.Describe(CultureInfo.InvariantCulture));
        Assert.Equal("Önce: Profil uygulandı", info.Describe(Turkish));
    }

    [Fact]
    public void The_english_prefix_written_by_older_versions_is_not_shown_twice()
    {
        var info = new SnapshotInfo("id", DateTimeOffset.UnixEpoch, "Before: Profil uygulandı", Guid.NewGuid());

        Assert.Equal("Önce: Profil uygulandı", info.Describe(Turkish));
        Assert.Equal("Before: Profil uygulandı", info.Describe(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Other_reasons_are_shown_as_written()
    {
        var info = new SnapshotInfo("id", DateTimeOffset.UnixEpoch, "Before: manual", PlanId: null);

        Assert.Equal("Before: manual", info.Describe(Turkish));
    }
}
