using EnvKit.Core.IO;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Paths;

public sealed class PathAnalyzer(IFileSystem fileSystem)
{
    public const string VariableName = "Path";

    public PathReport Analyze(IEnvStore store, EnvLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(store);
        return Analyze(store.Get(VariableName, EnvScope.Machine), store.Get(VariableName, EnvScope.User), lookup);
    }

    public PathReport Analyze(EnvVariable? machinePath, EnvVariable? userPath, EnvLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        var entries = new List<PathEntry>();
        var issues = new List<PathIssue>();
        var firstByDirectory = new Dictionary<string, PathEntry>(StringComparer.OrdinalIgnoreCase);

        // Windows builds the effective PATH as machine entries followed by user entries.
        foreach (var variable in new[] { machinePath, userPath })
        {
            if (variable is null)
            {
                continue;
            }

            if (variable.ExceedsMaxLength)
            {
                issues.Add(new PathIssue(PathIssueKind.ValueTooLong, variable.Scope, Detail: $"{variable.Value.Length}"));
            }

            var segments = PathList.Split(variable.Value);
            for (var index = 0; index < segments.Count; index++)
            {
                var entry = Analyze(segments[index], index, variable, lookup, firstByDirectory, issues);
                entries.Add(entry);
            }
        }

        return new PathReport(entries, issues);
    }

    private PathEntry Analyze(
        string raw,
        int index,
        EnvVariable variable,
        EnvLookup lookup,
        Dictionary<string, PathEntry> firstByDirectory,
        List<PathIssue> issues)
    {
        var scope = variable.Scope;
        Expansion? expansion = variable.Kind == EnvValueKind.Expandable ? lookup.Expand(raw, scope) : null;
        var expanded = expansion?.Value ?? raw;
        var entry = new PathEntry(raw, expanded, PathList.Normalize(expanded), scope, index);

        void Report(PathIssueKind kind, PathEntry? duplicateOf = null, string? detail = null) =>
            issues.Add(new PathIssue(kind, scope, entry, duplicateOf, detail));

        if (entry.Directory.Length == 0)
        {
            Report(PathIssueKind.Empty);
            return entry;
        }

        if (raw.Contains('"', StringComparison.Ordinal))
        {
            Report(PathIssueKind.Quoted);
        }

        // Duplicates are judged on what Windows will actually search.
        if (firstByDirectory.TryGetValue(entry.Directory, out var first))
        {
            Report(PathIssueKind.Duplicate, duplicateOf: first);
        }
        else
        {
            firstByDirectory.Add(entry.Directory, entry);
        }

        // The checks below each explain why the directory cannot be found, so only one applies.
        if (expansion is null && EnvExpander.HasReferences(raw))
        {
            Report(PathIssueKind.NotExpanded);
        }
        else if (expansion is { IsComplete: false })
        {
            Report(PathIssueKind.UnresolvedVariable, detail: string.Join(", ", expansion.Unresolved));
        }
        else if (!PathList.IsFullyQualified(entry.Directory))
        {
            Report(PathIssueKind.NotFullyQualified);
        }
        else if (!fileSystem.DirectoryExists(entry.Directory))
        {
            Report(PathIssueKind.Missing);
        }

        return entry;
    }
}
