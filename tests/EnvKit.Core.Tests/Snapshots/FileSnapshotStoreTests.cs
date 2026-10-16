using System.Text;
using EnvKit.Core.Snapshots;
using EnvKit.Core.Tests.Fakes;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Tests.Snapshots;

public sealed class FileSnapshotStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "EnvKit.Tests", Guid.NewGuid().ToString("N"));
    private readonly XorProtector _protector = new();
    private readonly InMemoryEnvStore _env = new();
    private readonly SteppingTimeProvider _time = new();

    public FileSnapshotStoreTests() =>
        _env.Set(new EnvVariable("GITHUB_TOKEN", "ghp_secret_value", EnvScope.User));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private Snapshot Capture(string reason) => Snapshot.Capture(_env, reason, timeProvider: _time);

    [Fact]
    public void Saves_lists_and_loads()
    {
        var store = new FileSnapshotStore(_directory, _protector);
        var first = Capture("first");
        var second = Capture("second");

        store.Save(first);
        store.Save(second);

        Assert.Equal([second.Info, first.Info], store.List());
        Assert.Equal(first.Variables, store.Load(first.Id).Variables);
    }

    [Fact]
    public void Files_are_not_plain_text()
    {
        new FileSnapshotStore(_directory, _protector).Save(Capture("t"));

        var bytes = File.ReadAllBytes(Directory.GetFiles(_directory).Single());
        Assert.DoesNotContain("ghp_secret_value", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.True(_protector.Calls > 0);
    }

    [Fact]
    public void Keeps_only_the_newest_snapshots()
    {
        var store = new FileSnapshotStore(_directory, _protector, retain: 2);
        var snapshots = Enumerable.Range(0, 4).Select(i => Capture($"s{i}")).ToList();

        snapshots.ForEach(store.Save);

        Assert.Equal(["s3", "s2"], store.List().Select(i => i.Reason));
    }

    [Fact]
    public void Snapshots_taken_back_to_back_keep_their_order()
    {
        var store = new FileSnapshotStore(_directory, _protector, retain: 2);

        // Real clock: these land within the same millisecond.
        for (var i = 0; i < 4; i++)
        {
            store.Save(Snapshot.Capture(_env, $"s{i}"));
        }

        Assert.Equal(["s3", "s2"], store.List().Select(i => i.Reason));
    }

    [Fact]
    public void Skips_unreadable_files()
    {
        var store = new FileSnapshotStore(_directory, _protector);
        store.Save(Capture("good"));
        File.WriteAllText(Path.Combine(_directory, "00000000-garbage.snapshot"), "not json");

        Assert.Equal(["good"], store.List().Select(i => i.Reason));
    }

    [Theory]
    [InlineData(@"..\..\secrets")]
    [InlineData("a/b")]
    public void Rejects_ids_that_escape_the_directory(string id) =>
        Assert.Throws<ArgumentException>(() => new FileSnapshotStore(_directory, _protector).Load(id));

    [Fact]
    public void Unknown_id_is_not_found() =>
        Assert.Throws<KeyNotFoundException>(() => new FileSnapshotStore(_directory, _protector).Load("20260101-000000-000-abcdef"));

    [Fact]
    public void Missing_directory_lists_nothing() =>
        Assert.Empty(new FileSnapshotStore(_directory, _protector).List());
}
