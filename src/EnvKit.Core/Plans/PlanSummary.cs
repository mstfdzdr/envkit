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
        return [.. plan.Changes.Select(change => change switch
        {
            VariableChange v => Summarize(v),
            CredentialChange c => Summarize(c),
            _ => new ChangeSummary(change.Subject, string.Empty, []),
        })];
    }

    /// <summary>Describes what changes about the secret without ever including it.</summary>
    public static ChangeSummary Summarize(CredentialChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var (before, after) = (change.Before, change.After);
        var lines = new List<DiffLine>();

        void Field(string label, string? old, string? @new)
        {
            if (old == @new)
            {
                return;
            }

            if (old is not null)
            {
                lines.Add(new DiffLine(DiffKind.Removed, $"{label}: {old}"));
            }

            if (@new is not null)
            {
                lines.Add(new DiffLine(DiffKind.Added, $"{label}: {@new}"));
            }
        }

        Field("user", before?.UserName, after?.UserName);
        Field("comment", before?.Comment, after?.Comment);
        Field("persistence", before?.Persistence.ToString(), after?.Persistence.ToString());

        var secretChanged = before?.Secret is null != after?.Secret is null
            || (before?.Secret is { } b && after?.Secret is { } a && !b.AsSpan().SequenceEqual(a));
        if (after is not null && secretChanged)
        {
            lines.Add(new DiffLine(DiffKind.Added, before is null ? "password: set" : "password: changed"));
        }

        var note = (before, after) switch
        {
            (null, _) => "new",
            (_, null) when change.IsIrreversible => "delete; Windows never reveals this secret, so it cannot be restored",
            (_, null) => "delete",
            _ => string.Empty,
        };
        return new ChangeSummary(change.Subject, note, lines);
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
