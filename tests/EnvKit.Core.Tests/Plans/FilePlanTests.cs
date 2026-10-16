using System.Text;
using EnvKit.Core.DotEnv;
using EnvKit.Core.IO;
using EnvKit.Core.Plans;
using EnvKit.Testing;

namespace EnvKit.Core.Tests.Plans;

public sealed class FilePlanTests : IDisposable
{
    private const string Original = "# app settings\r\nDB_HOST=localhost   # dev only\r\nAPI_KEY='sk-old'\r\n";

    private readonly string _env = Path.GetFullPath(@"C:\repo\.env");
    private readonly string _secrets = Path.GetFullPath(@"C:\Users\me\AppData\Roaming\Microsoft\UserSecrets\abc\secrets.json");
    private readonly InMemoryEnvStore _store = new();
    private readonly InMemoryFileStore _files = new();
    private readonly InMemorySnapshotStore _snapshots = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "EnvKit.Tests", Guid.NewGuid().ToString("N"));

    public FilePlanTests() => _files.Seed(_env, Original);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private PlanBuilder Builder() => new(_store, files: _files);

    private PlanResult Apply(ChangePlan plan) => new PlanApplier(_store, _snapshots, null, files: _files).Apply(plan);

    [Fact]
    public void Editing_a_dotenv_file_touches_only_the_changed_line_and_can_be_undone()
    {
        var result = Apply(Builder().EditDotEnv(_env, d => d.Set("DB_HOST", "db.internal")).Build("edit"));

        Assert.Equal(Original.Replace("DB_HOST=localhost", "DB_HOST=db.internal", StringComparison.Ordinal), _files.Text(_env));
        Assert.Equal(Encoding.UTF8.GetBytes(Original), Assert.Single(_snapshots.Saved).FileList.Single().Content);

        Apply(Builder().Restore(_snapshots.Load(result.Snapshot!.Id)).Build("undo"));

        Assert.Equal(Original, _files.Text(_env));
    }

    [Fact]
    public void Creating_a_file_then_restoring_deletes_it()
    {
        var path = Path.GetFullPath(@"C:\repo\.env.local");
        var plan = Builder().EditDotEnv(path, d => d.Set("A", "1")).Build("create");
        Assert.Null(Assert.IsType<FileChange>(Assert.Single(plan.Changes)).Before);

        var result = Apply(plan);
        Assert.Equal("A=1" + Environment.NewLine, _files.Text(path));

        Apply(Builder().Restore(_snapshots.Load(result.Snapshot!.Id)).Build("undo"));
        Assert.Null(_files.Read(path));
    }

    [Fact]
    public void Repeated_edits_build_on_each_other_and_no_op_edits_vanish()
    {
        Assert.True(Builder().EditDotEnv(_env, d => d.Set("DB_HOST", "localhost")).Build("t").IsEmpty);

        var plan = Builder()
            .EditDotEnv(_env, d => d.Set("A", "1"))
            .EditDotEnv(_env, d => d.Rename("A", "B"))
            .Build("t");

        var change = Assert.IsType<FileChange>(Assert.Single(plan.Changes));
        Assert.EndsWith("B=1\r\n", Encoding.UTF8.GetString(change.After!), StringComparison.Ordinal);
    }

    [Fact]
    public void File_edited_after_the_preview_is_a_conflict()
    {
        var plan = Builder().EditDotEnv(_env, d => d.Remove("API_KEY")).Build("t");
        _files.Seed(_env, Original + "ADDED_BY_EDITOR=1\r\n");

        Assert.Throws<PlanConflictException>(() => Apply(plan));

        Assert.Contains("ADDED_BY_EDITOR", _files.Text(_env), StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_file_write_reverts_the_others()
    {
        var other = Path.GetFullPath(@"C:\repo\.env.test");
        _files.Seed(other, "X=1\n");
        _files.FailOnWrite.Add(other);
        var plan = Builder()
            .EditDotEnv(_env, d => d.Set("DB_HOST", "changed"))
            .EditDotEnv(other, d => d.Set("X", "2"))
            .Build("t");

        Assert.Throws<IOException>(() => Apply(plan));

        Assert.Equal(Original, _files.Text(_env));
    }

    [Fact]
    public void User_secrets_are_edited_through_plans_too()
    {
        Apply(Builder().EditUserSecrets(_secrets, d => d.Set("Stripe:ApiKey", "sk_test")).Build("t"));

        Assert.Equal("sk_test", UserSecretsDocument.Parse(_files.Read(_secrets)).Get("stripe:apikey"));
    }

    [Fact]
    public void Summary_lists_keys_and_hides_secret_values()
    {
        var plan = Builder()
            .EditDotEnv(_env, d =>
            {
                d.Set("DB_HOST", "db.internal");
                d.Set("API_KEY", "sk-new");
                d.Set("FEATURE_X", "on");
                d.Remove("NOPE");
            })
            .Build("t");

        var summary = PlanSummary.Summarize(plan).Single();
        var lines = summary.Lines.Select(l => $"{(l.Kind == DiffKind.Added ? '+' : '-')} {l.Text}").ToList();

        Assert.Equal(["- DB_HOST = localhost", "+ DB_HOST = db.internal", "+ API_KEY: changed", "+ FEATURE_X = on"], lines);
        Assert.DoesNotContain(lines, l => l.Contains("sk-", StringComparison.Ordinal));
        Assert.StartsWith(".env ", summary.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Physical_writes_are_atomic_replacements_without_leftovers()
    {
        var store = new PhysicalFileStore();
        var path = Path.Combine(_directory, "nested", ".env");

        store.Write(path, [1, 2, 3]);
        store.Write(path, [4]);

        Assert.Equal([4], store.Read(path));
        Assert.Equal([".env"], Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName));
        Assert.True(store.Delete(path));
        Assert.Null(store.Read(path));
    }
}
