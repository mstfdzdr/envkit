using System.CommandLine;
using EnvKit.Core.Paths;
using EnvKit.Core.Variables;

namespace EnvKit.Cli.Commands;

internal static class PathCommands
{
    public static Command Create(CliServices services) => new("path", "Inspect and edit PATH.")
    {
        List(services),
        Add(services),
        Remove(services),
        Fix(services),
    };

    private static Command List(CliServices services)
    {
        var scope = Options.ScopeFilter();
        var check = new Option<bool>("--check")
        {
            Description = "Only show entries with problems; exit with 1 if there are any.",
        };
        var command = new Command("list", "List PATH entries in search order, with problems marked.") { scope, check };
        command.SetAction(result =>
        {
            var output = services.Out;
            var report = services.AnalyzePath();
            var onlyProblems = result.GetValue(check);

            foreach (var current in Options.Scopes(result.GetValue(scope)))
            {
                var variable = services.Store.Get(PathAnalyzer.VariableName, current);
                if (variable is null)
                {
                    output.WriteLine($"{current} PATH: not set");
                    continue;
                }

                var entries = report.EntriesIn(current).ToList();
                output.WriteLine($"{current} PATH ({Format.Kind(variable.Kind)}, {entries.Count} entries)");
                foreach (var issue in report.Issues.Where(i => i.Scope == current && i.Entry is null))
                {
                    output.WriteLine($"  [{PathIssueText.Describe(issue)}]");
                }

                foreach (var entry in entries)
                {
                    var issues = report.IssuesFor(entry).ToList();
                    if (onlyProblems && issues.Count == 0)
                    {
                        continue;
                    }

                    var tags = issues.Count == 0 ? string.Empty : $"   [{string.Join(", ", issues.Select(PathIssueText.Describe))}]";
                    output.WriteLine($"  {entry.Index,3}  {Format.Entry(entry.Raw)}{tags}");
                }
            }

            if (!onlyProblems)
            {
                return ExitCodes.Ok;
            }

            if (report.Issues.Count == 0)
            {
                output.WriteLine("No problems found.");
                return ExitCodes.Ok;
            }

            output.WriteLine($"{report.Issues.Count} problem(s). 'envkit path fix' removes empty and duplicate entries.");
            return ExitCodes.Error;
        });
        return command;
    }

    private static Command Add(CliServices services)
    {
        var directory = new Argument<string>("directory") { Description = @"Directory to add; may use %VARIABLES%." };
        var scope = Options.Scope();
        var prepend = new Option<bool>("--prepend") { Description = "Put it first so it wins over existing entries." };
        var dryRun = Options.DryRun();
        var yes = Options.Yes();
        var command = new Command("add", "Add a directory to PATH.") { directory, scope, prepend, dryRun, yes };
        command.SetAction(result =>
        {
            var path = result.GetRequiredValue(directory);
            var target = result.GetRequiredValue(scope);
            var builder = services.NewPlan();
            if (!builder.AddPathEntry(path, target, result.GetValue(prepend)))
            {
                services.Out.WriteLine($"Already in {target} PATH.");
                return ExitCodes.Ok;
            }

            var expanded = PathList.Normalize(services.Lookup().Expand(path, target).Value);
            if (!services.FileSystem.DirectoryExists(expanded))
            {
                services.Out.WriteLine($"Note: {expanded} does not exist (yet).");
            }

            return new PlanRunner(services).Run(
                builder.Build($"Add {path} to {target} PATH"), result.GetValue(dryRun), result.GetValue(yes));
        });
        return command;
    }

    private static Command Remove(CliServices services)
    {
        var directory = new Argument<string>("directory") { Description = "Directory to remove, as stored or as expanded." };
        var scope = Options.Scope();
        var dryRun = Options.DryRun();
        var yes = Options.Yes();
        var command = new Command("remove", "Remove a directory from PATH.") { directory, scope, dryRun, yes };
        command.SetAction(result =>
        {
            var path = result.GetRequiredValue(directory);
            var target = result.GetRequiredValue(scope);
            var wanted = PathList.Normalize(path);

            // Match "%USERPROFILE%\bin" whether the user typed it that way or expanded.
            var entries = services.AnalyzePath().EntriesIn(target).ToList();
            var kept = entries
                .Where(e => !wanted.Equals(PathList.Normalize(e.Raw), StringComparison.OrdinalIgnoreCase)
                    && !wanted.Equals(e.Directory, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Raw)
                .ToList();
            if (kept.Count == entries.Count)
            {
                services.Error.WriteLine($"Not in {target} PATH: {path}");
                return ExitCodes.Error;
            }

            var plan = services.NewPlan().SetPath(target, kept).Build($"Remove {path} from {target} PATH");
            return new PlanRunner(services).Run(plan, result.GetValue(dryRun), result.GetValue(yes));
        });
        return command;
    }

    private static Command Fix(CliServices services)
    {
        var scope = Options.ScopeFilter();
        var removeMissing = new Option<bool>("--remove-missing")
        {
            Description = "Also remove directories that do not exist (check removable and network drives first).",
        };
        var unquote = new Option<bool>("--unquote") { Description = "Also strip quotes from entries." };
        var dryRun = Options.DryRun();
        var yes = Options.Yes();
        var command = new Command("fix", "Remove empty and duplicate PATH entries.") { scope, removeMissing, unquote, dryRun, yes };
        command.SetAction(result =>
        {
            var fixes = PathFixes.RemoveEmpty | PathFixes.RemoveDuplicates;
            if (result.GetValue(removeMissing))
            {
                fixes |= PathFixes.RemoveMissing;
            }

            if (result.GetValue(unquote))
            {
                fixes |= PathFixes.Unquote;
            }

            var scopes = Options.Scopes(result.GetValue(scope)).ToHashSet();
            var cleaned = PathCleanup.Apply(services.AnalyzePath(), fixes)
                .Where(pair => scopes.Contains(pair.Key))
                .ToDictionary();
            var plan = services.NewPlan().ApplyCleanup(cleaned).Build("Clean up PATH");
            return new PlanRunner(services).Run(plan, result.GetValue(dryRun), result.GetValue(yes));
        });
        return command;
    }
}
