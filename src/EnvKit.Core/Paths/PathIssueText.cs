using EnvKit.Core.Variables;

namespace EnvKit.Core.Paths;

public static class PathIssueText
{
    public static string Describe(PathIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return issue.Kind switch
        {
            PathIssueKind.Empty => "empty",
            PathIssueKind.Missing => "missing",
            PathIssueKind.Duplicate => $"duplicate of {issue.DuplicateOf!.Scope}#{issue.DuplicateOf.Index}",
            PathIssueKind.Quoted => "quoted",
            PathIssueKind.UnresolvedVariable => $"unresolved: {issue.Detail}",
            PathIssueKind.NotExpanded => "not expanded: PATH is REG_SZ",
            PathIssueKind.NotFullyQualified => "relative path",
            PathIssueKind.ValueTooLong => $"value too long: {issue.Detail} > {EnvVariable.MaxValueLength} characters",
            _ => issue.Kind.ToString(),
        };
    }
}
