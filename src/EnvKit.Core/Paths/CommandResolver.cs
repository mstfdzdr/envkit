using EnvKit.Core.IO;

namespace EnvKit.Core.Paths;

public sealed record CommandMatch(string FullPath, PathEntry Entry)
{
    /// <summary>
    /// A Store "app execution alias" stub, e.g. the python.exe that opens the Store
    /// instead of running Python.
    /// </summary>
    public bool IsAppExecutionAlias =>
        Entry.Directory.EndsWith(@"\Microsoft\WindowsApps", StringComparison.OrdinalIgnoreCase);
}

public sealed class CommandResolver(IFileSystem fileSystem)
{
    public const string DefaultPathExt = ".COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC";

    /// <summary>
    /// Every match in PATH search order; the first one is what runs, the rest are shadowed.
    /// The current directory, which cmd.exe searches first, is not considered.
    /// </summary>
    public IReadOnlyList<CommandMatch> FindAll(string command, PathReport report, string? pathExt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(report);
        if (command.IndexOfAny(['\\', '/', ':']) >= 0)
        {
            throw new ArgumentException("Expected a bare command name, not a path.", nameof(command));
        }

        var candidates = CandidateNames(command, pathExt ?? DefaultPathExt);
        var matches = new List<CommandMatch>();
        foreach (var entry in report.Entries.Where(e => IsSearchable(report, e)))
        {
            foreach (var name in candidates)
            {
                // Not Path.Join: Core also builds on non-Windows hosts, where the separator differs.
                var fullPath = entry.Directory.EndsWith('\\') ? entry.Directory + name : $@"{entry.Directory}\{name}";
                if (fileSystem.FileExists(fullPath))
                {
                    matches.Add(new CommandMatch(fullPath, entry));
                }
            }
        }

        return matches;
    }

    private static List<string> CandidateNames(string command, string pathExt)
    {
        var extensions = pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var extension = Path.GetExtension(command);

        // "git.exe" is looked up as-is; "git" is tried with each PATHEXT extension in order.
        return extension.Length > 0 && extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            ? [command]
            : [.. extensions.Select(e => command + e.ToLowerInvariant())];
    }

    private static bool IsSearchable(PathReport report, PathEntry entry) =>
        !report.IssuesFor(entry).Any(i => i.Kind is
            PathIssueKind.Empty or PathIssueKind.Duplicate or PathIssueKind.Missing
            or PathIssueKind.NotFullyQualified or PathIssueKind.UnresolvedVariable or PathIssueKind.NotExpanded);
}
