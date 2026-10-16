using EnvKit.Core.Paths;
using EnvKit.Core.Tests.Fakes;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Tests.Paths;

public class PathCleanupTests
{
    private static PathReport Analyze(string machine, string user)
    {
        var store = new InMemoryEnvStore();
        store.Set(new EnvVariable("Path", machine, EnvScope.Machine, EnvValueKind.Expandable));
        store.Set(new EnvVariable("Path", user, EnvScope.User, EnvValueKind.Expandable));
        var fileSystem = new FakeFileSystem().WithDirectories(@"C:\a", @"C:\b", @"C:\a;b");
        return new PathAnalyzer(fileSystem).Analyze(store, new EnvLookup(store, _ => null));
    }

    [Fact]
    public void Unchanged_scopes_are_omitted() =>
        Assert.Empty(PathCleanup.Apply(Analyze(@"C:\a", @"C:\b"), PathFixes.RemoveEmpty | PathFixes.RemoveDuplicates));

    [Fact]
    public void Removes_only_the_requested_problems()
    {
        var report = Analyze(@"C:\a;;C:\gone;", @"C:\b;C:\A\");

        var emptyOnly = PathCleanup.Apply(report, PathFixes.RemoveEmpty);
        var everything = PathCleanup.Apply(report, PathFixes.RemoveEmpty | PathFixes.RemoveDuplicates | PathFixes.RemoveMissing);

        Assert.Equal(@"C:\a;C:\gone", emptyOnly[EnvScope.Machine]);
        Assert.False(emptyOnly.ContainsKey(EnvScope.User));
        Assert.Equal(@"C:\a", everything[EnvScope.Machine]);
        Assert.Equal(@"C:\b", everything[EnvScope.User]);
    }

    [Fact]
    public void Keeps_raw_text_of_untouched_entries()
    {
        var report = Analyze(@"%NOPE%\x; C:\a ;;C:\b", @"C:\b");

        var result = PathCleanup.Apply(report, PathFixes.RemoveEmpty | PathFixes.RemoveMissing);

        // %NOPE% is unresolved rather than missing, so it stays.
        Assert.Equal(@"%NOPE%\x; C:\a ;C:\b", result[EnvScope.Machine]);
    }

    [Fact]
    public void Unquotes_unless_the_quotes_protect_a_semicolon()
    {
        var report = Analyze(@"""C:\a"";""C:\a;b""", @"C:\b");

        var result = PathCleanup.Apply(report, PathFixes.Unquote);

        Assert.Equal(@"C:\a;""C:\a;b""", result[EnvScope.Machine]);
    }
}
