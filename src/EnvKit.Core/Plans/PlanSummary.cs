using EnvKit.Core.Paths;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Plans;

/// <param name="Title">E.g. <c>User\Path</c>.</param>
/// <param name="Note">E.g. "new", "delete" or a kind change; empty when there is nothing to add.</param>
/// <param name="Lines">Only the added and removed lines; PATH changes are diffed per entry.</param>
public sealed record ChangeSummary(string Title, string Note, IReadOnlyList<DiffLine> Lines);

/// <summary>The human-readable form of a plan, shared by the CLI and the app.</summary>
public static class PlanSummary
{
    public static IReadOnlyList<ChangeSummary> Summarize(ChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return [.. plan.Changes.Cast<VariableChange>().Select(Summarize)];
    }

    public static ChangeSummary Summarize(VariableChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var lines = EnvVariable.NameComparer.Equals(change.Name, PathAnalyzer.VariableName)
            ? [.. PathDiff.Compute(change.Before?.Value, change.After?.Value).Where(l => l.Kind != DiffKind.Unchanged)]
            : ValueLines(change);
        return new ChangeSummary($@"{change.Scope}\{change.Name}", Note(change), lines);
    }

    public static string RegistryName(this EnvValueKind kind) =>
        kind == EnvValueKind.Expandable ? "REG_EXPAND_SZ" : "REG_SZ";

    private static List<DiffLine> ValueLines(VariableChange change)
    {
        var lines = new List<DiffLine>();
        if (change.Before is { } before)
        {
            lines.Add(new DiffLine(DiffKind.Removed, before.Value));
        }

        if (change.After is { } after)
        {
            lines.Add(new DiffLine(DiffKind.Added, after.Value));
        }

        return lines;
    }

    private static string Note(VariableChange change) => (change.Before, change.After) switch
    {
        (null, _) => "new",
        (_, null) => "delete",
        ({ } b, { } a) when b.Kind != a.Kind => $"{b.Kind.RegistryName()} -> {a.Kind.RegistryName()}",
        _ => string.Empty,
    };
}
