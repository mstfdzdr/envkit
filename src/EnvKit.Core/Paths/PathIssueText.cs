using EnvKit.Core.Variables;

namespace EnvKit.Core.Paths;

public static class PathIssueText
{
    public static string Describe(PathIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return issue.Kind switch
        {
            PathIssueKind.Duplicate => CoreText.Format("Issue_Duplicate", CoreText.Scope(issue.DuplicateOf!.Scope), issue.DuplicateOf.Index),
            PathIssueKind.UnresolvedVariable => CoreText.Format("Issue_UnresolvedVariable", issue.Detail),
            PathIssueKind.ValueTooLong => CoreText.Format("Issue_ValueTooLong", issue.Detail, EnvVariable.MaxValueLength),
            _ => CoreText.Get($"Issue_{issue.Kind}"),
        };
    }
}
