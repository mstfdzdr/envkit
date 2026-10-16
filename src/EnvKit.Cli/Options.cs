using System.CommandLine;
using EnvKit.Core.Variables;

namespace EnvKit.Cli;

internal static class Options
{
    public static Option<EnvScope> Scope(string description = "User or Machine.") => new("--scope", "-s")
    {
        Description = description,
        DefaultValueFactory = _ => EnvScope.User,
    };

    /// <summary>No default: leaving it out means "both scopes".</summary>
    public static Option<EnvScope?> ScopeFilter() => new("--scope", "-s")
    {
        Description = "Only User or Machine (default: both).",
    };

    public static Option<bool> DryRun() => new("--dry-run", "-n")
    {
        Description = "Show the changes without writing them.",
    };

    public static Option<bool> Yes() => new("--yes", "-y")
    {
        Description = "Apply without asking for confirmation.",
    };

    public static IEnumerable<EnvScope> Scopes(EnvScope? filter) =>
        filter is { } only ? [only] : [EnvScope.Machine, EnvScope.User];
}
