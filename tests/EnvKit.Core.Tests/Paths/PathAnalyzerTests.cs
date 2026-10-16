using EnvKit.Core.Paths;
using EnvKit.Core.Tests.Fakes;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Tests.Paths;

public class PathAnalyzerTests
{
    private readonly InMemoryEnvStore _store = new();
    private readonly FakeFileSystem _fileSystem = new FakeFileSystem()
        .WithDirectories(@"C:\Windows\system32", @"C:\tools", @"C:\Users\me\bin", @"C:\jdk\bin");

    private readonly Dictionary<string, string> _process = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SystemRoot"] = @"C:\Windows",
        ["USERPROFILE"] = @"C:\Users\me",
    };

    private PathReport Analyze(string? machine, string? user, EnvValueKind kind = EnvValueKind.Expandable)
    {
        if (machine is not null)
        {
            _store.Set(new EnvVariable("Path", machine, EnvScope.Machine, kind));
        }

        if (user is not null)
        {
            _store.Set(new EnvVariable("Path", user, EnvScope.User, kind));
        }

        return new PathAnalyzer(_fileSystem).Analyze(_store, new EnvLookup(_store, _process.GetValueOrDefault));
    }

    private static PathIssueKind[] KindsFor(PathReport report, int position) =>
        [.. report.IssuesFor(report.Entries[position]).Select(i => i.Kind)];

    [Fact]
    public void Healthy_path_has_no_issues()
    {
        var report = Analyze(@"%SystemRoot%\system32;C:\tools", @"%USERPROFILE%\bin");

        Assert.Empty(report.Issues);
        Assert.Equal(3, report.Entries.Count);
    }

    [Fact]
    public void Orders_machine_entries_before_user_entries()
    {
        var report = Analyze(@"C:\tools", @"C:\Users\me\bin");

        Assert.Equal([EnvScope.Machine, EnvScope.User], report.Entries.Select(e => e.Scope));
        Assert.Equal(@"C:\Windows\system32", Analyze(@"%SystemRoot%\system32", null).Entries[0].Expanded);
    }

    [Fact]
    public void Flags_empty_segments()
    {
        var report = Analyze(@"C:\tools;;C:\Windows\system32;", null);

        Assert.Equal([PathIssueKind.Empty], KindsFor(report, 1));
        Assert.Equal([PathIssueKind.Empty], KindsFor(report, 3));
    }

    [Fact]
    public void Flags_missing_directories()
    {
        var report = Analyze(@"C:\gone", null);

        Assert.Equal([PathIssueKind.Missing], KindsFor(report, 0));
    }

    [Fact]
    public void Flags_later_duplicate_and_points_to_the_winner()
    {
        var report = Analyze(@"C:\tools", @"c:\TOOLS\");

        var issue = Assert.Single(report.Issues);
        Assert.Equal(PathIssueKind.Duplicate, issue.Kind);
        Assert.Equal(EnvScope.User, issue.Scope);
        Assert.Same(report.Entries[0], issue.DuplicateOf);
    }

    [Fact]
    public void Duplicates_are_detected_after_expansion()
    {
        var report = Analyze(@"C:\Users\me\bin", @"%USERPROFILE%\bin");

        Assert.Equal([PathIssueKind.Duplicate], KindsFor(report, 1));
    }

    [Fact]
    public void Flags_quotes_but_still_checks_the_directory()
    {
        var report = Analyze(@"""C:\tools"";""C:\gone""", null);

        Assert.Equal([PathIssueKind.Quoted], KindsFor(report, 0));
        Assert.Equal([PathIssueKind.Quoted, PathIssueKind.Missing], KindsFor(report, 1));
    }

    [Fact]
    public void Machine_path_referencing_a_user_only_variable_is_unresolved()
    {
        _store.Set(new EnvVariable("JAVA_HOME", @"C:\jdk", EnvScope.User));

        var report = Analyze(@"%JAVA_HOME%\bin", @"%JAVA_HOME%\bin");

        var machineIssue = Assert.Single(report.IssuesFor(report.Entries[0]));
        Assert.Equal(PathIssueKind.UnresolvedVariable, machineIssue.Kind);
        Assert.Equal("JAVA_HOME", machineIssue.Detail);
        Assert.Empty(report.IssuesFor(report.Entries[1]));
    }

    [Fact]
    public void References_in_a_REG_SZ_path_are_not_expanded()
    {
        var report = Analyze(@"%SystemRoot%\system32", null, EnvValueKind.Literal);

        Assert.Equal([PathIssueKind.NotExpanded], KindsFor(report, 0));
        Assert.Equal(@"%SystemRoot%\system32", report.Entries[0].Expanded);
    }

    [Theory]
    [InlineData("bin")]
    [InlineData(".")]
    [InlineData(@"C:tools")]
    public void Flags_paths_relative_to_the_current_directory(string entry) =>
        Assert.Equal([PathIssueKind.NotFullyQualified], KindsFor(Analyze(entry, null), 0));

    [Fact]
    public void Flags_overlong_value()
    {
        var report = Analyze(null, @"C:\tools;" + new string('x', EnvVariable.MaxValueLength));

        Assert.Contains(report.Issues, i => i is { Kind: PathIssueKind.ValueTooLong, Scope: EnvScope.User, Entry: null });
    }

    [Fact]
    public void Missing_path_variables_yield_an_empty_report()
    {
        var report = Analyze(null, null);

        Assert.Empty(report.Entries);
        Assert.Empty(report.Issues);
    }
}
