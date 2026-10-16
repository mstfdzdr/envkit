using EnvKit.Core.Variables;

namespace EnvKit.Core.Paths;

[Flags]
public enum PathFixes
{
    None = 0,
    RemoveEmpty = 1,
    RemoveDuplicates = 2,
    RemoveMissing = 4,
    Unquote = 8,
}

public static class PathCleanup
{
    /// <summary>
    /// Computes the new PATH value per scope. Scopes whose value would not change are omitted.
    /// Untouched entries keep their exact raw text. Entries with unresolved or unexpanded
    /// references are never treated as missing: fixing the reference may bring them back.
    /// </summary>
    public static IReadOnlyDictionary<EnvScope, string> Apply(PathReport report, PathFixes fixes)
    {
        ArgumentNullException.ThrowIfNull(report);

        var result = new Dictionary<EnvScope, string>();
        foreach (var scope in new[] { EnvScope.Machine, EnvScope.User })
        {
            var entries = report.EntriesIn(scope).ToList();
            if (entries.Count == 0)
            {
                continue;
            }

            var kept = new List<string>();
            foreach (var entry in entries)
            {
                if (ShouldRemove(report, entry, fixes))
                {
                    continue;
                }

                kept.Add(fixes.HasFlag(PathFixes.Unquote) ? Unquote(entry.Raw) : entry.Raw);
            }

            var original = PathList.Join(entries.Select(e => e.Raw));
            var updated = PathList.Join(kept);
            if (updated != original)
            {
                result[scope] = updated;
            }
        }

        return result;
    }

    private static bool ShouldRemove(PathReport report, PathEntry entry, PathFixes fixes) =>
        (fixes.HasFlag(PathFixes.RemoveEmpty) && report.Has(entry, PathIssueKind.Empty))
        || (fixes.HasFlag(PathFixes.RemoveDuplicates) && report.Has(entry, PathIssueKind.Duplicate))
        || (fixes.HasFlag(PathFixes.RemoveMissing) && report.Has(entry, PathIssueKind.Missing));

    private static string Unquote(string raw)
    {
        var unquoted = raw.Replace("\"", string.Empty, StringComparison.Ordinal);

        // Quotes are what keep a ';' inside the directory name.
        return unquoted.Contains(';', StringComparison.Ordinal) ? raw : unquoted;
    }
}
