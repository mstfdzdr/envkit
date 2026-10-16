using EnvKit.Core.Variables;

namespace EnvKit.Core.Paths;

/// <param name="Raw">The segment exactly as stored.</param>
/// <param name="Expanded">With references resolved (unchanged for REG_SZ values, which Windows never expands).</param>
/// <param name="Directory">Normalized <paramref name="Expanded"/>; see <see cref="PathList.Normalize"/>.</param>
/// <param name="Index">Position within its scope's PATH.</param>
public sealed record PathEntry(string Raw, string Expanded, string Directory, EnvScope Scope, int Index);

public enum PathIssueKind
{
    /// <summary>Empty segment, e.g. from <c>;;</c> or a trailing <c>;</c>.</summary>
    Empty,

    /// <summary>The directory does not exist.</summary>
    Missing,

    /// <summary>The same directory appears earlier in the effective PATH.</summary>
    Duplicate,

    /// <summary>Contains double quotes, which many programs do not strip.</summary>
    Quoted,

    /// <summary>A <c>%VAR%</c> reference has no value in this scope.</summary>
    UnresolvedVariable,

    /// <summary>Has <c>%VAR%</c> references but PATH is REG_SZ, so Windows will not expand them.</summary>
    NotExpanded,

    /// <summary>Relative to the current directory or drive (<c>bin</c>, <c>C:x</c>, <c>\x</c>).</summary>
    NotFullyQualified,

    /// <summary>The whole PATH value is longer than a process environment accepts.</summary>
    ValueTooLong,
}

/// <param name="Entry">The affected entry; <see langword="null"/> for value-level issues.</param>
/// <param name="DuplicateOf">For <see cref="PathIssueKind.Duplicate"/>, the earlier entry that wins.</param>
/// <param name="Detail">E.g. the unresolved variable names.</param>
public sealed record PathIssue(
    PathIssueKind Kind,
    EnvScope Scope,
    PathEntry? Entry = null,
    PathEntry? DuplicateOf = null,
    string? Detail = null);

/// <param name="Entries">Effective search order: machine entries, then user entries.</param>
public sealed record PathReport(IReadOnlyList<PathEntry> Entries, IReadOnlyList<PathIssue> Issues)
{
    public IEnumerable<PathIssue> IssuesFor(PathEntry entry) => Issues.Where(i => i.Entry == entry);

    public bool Has(PathEntry entry, PathIssueKind kind) => Issues.Any(i => i.Entry == entry && i.Kind == kind);

    public IEnumerable<PathEntry> EntriesIn(EnvScope scope) => Entries.Where(e => e.Scope == scope);
}
