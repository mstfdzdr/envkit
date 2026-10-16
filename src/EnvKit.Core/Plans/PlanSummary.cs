using EnvKit.Core.Credentials;
using EnvKit.Core.DotEnv;
using EnvKit.Core.Paths;
using EnvKit.Core.Security;
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
            FileChange f => Summarize(f),
            _ => new ChangeSummary(change.Subject, string.Empty, []),
        })];
    }

    /// <summary>Per-key diff of a .env or secrets.json file; values that look secret are never shown.</summary>
    public static ChangeSummary Summarize(FileChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var note = (change.Before, change.After) switch
        {
            (null, _) => CoreText.Get("Note_NewFile"),
            (_, null) => CoreText.Get("Note_DeleteFile"),
            _ => string.Empty,
        };

        var before = ReadKeys(change.Format, change.Before);
        var after = ReadKeys(change.Format, change.After);
        if (before is null || after is null)
        {
            return new ChangeSummary(change.Subject, note, [new DiffLine(DiffKind.Added, CoreText.Get("Line_ContentChanged"))]);
        }

        var comparer = change.Format == FileFormat.DotEnv ? StringComparer.Ordinal : UserSecretsDocument.KeyComparer;
        var old = before.ToDictionary(p => p.Key, p => p.Value, comparer);
        var @new = after.ToDictionary(p => p.Key, p => p.Value, comparer);

        static string Show(string key, string value) =>
            SecretDetector.LooksSecret(key, value) ? $"{key} = {SecretDetector.AsciiMask}" : $"{key} = {value}";

        var lines = new List<DiffLine>();
        foreach (var key in old.Keys.Concat(@new.Keys).Distinct(comparer))
        {
            var had = old.TryGetValue(key, out var oldValue);
            var has = @new.TryGetValue(key, out var newValue);
            if (had && has && oldValue == newValue)
            {
                continue;
            }

            if (had && has && (SecretDetector.LooksSecret(key, oldValue!) || SecretDetector.LooksSecret(key, newValue!)))
            {
                lines.Add(new DiffLine(DiffKind.Added, CoreText.Format("Line_KeyChanged", key)));
                continue;
            }

            if (had)
            {
                lines.Add(new DiffLine(DiffKind.Removed, Show(key, oldValue!)));
            }

            if (has)
            {
                lines.Add(new DiffLine(DiffKind.Added, Show(key, newValue!)));
            }
        }

        if (lines.Count == 0)
        {
            lines.Add(new DiffLine(DiffKind.Added, CoreText.Get("Line_FormattingChanged")));
        }

        return new ChangeSummary(change.Subject, note, lines);
    }

    private static IReadOnlyList<KeyValuePair<string, string>>? ReadKeys(FileFormat format, byte[]? content)
    {
        if (content is null)
        {
            return [];
        }

        try
        {
            return format == FileFormat.DotEnv
                ? DotEnvFile.Parse(content).Document.Values()
                : UserSecretsDocument.Parse(content).Values();
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
        {
            return null;
        }
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

        Field(CoreText.Get("Field_User"), before?.UserName, after?.UserName);
        Field(CoreText.Get("Field_Comment"), before?.Comment, after?.Comment);
        Field(CoreText.Get("Field_Persistence"), Persistence(before?.Persistence), Persistence(after?.Persistence));

        var secretChanged = before?.Secret is null != after?.Secret is null
            || (before?.Secret is { } b && after?.Secret is { } a && !b.AsSpan().SequenceEqual(a));
        if (after is not null && secretChanged)
        {
            lines.Add(new DiffLine(DiffKind.Added, CoreText.Get(before is null ? "Line_PasswordSet" : "Line_PasswordChanged")));
        }

        var note = (before, after) switch
        {
            (null, _) => CoreText.Get("Note_New"),
            (_, null) when change.IsIrreversible => CoreText.Get("Note_DeleteIrreversible"),
            (_, null) => CoreText.Get("Note_Delete"),
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
        return new ChangeSummary(change.Subject, Note(change), lines);
    }

    /// <summary>A display name for a persistence setting; <see langword="null"/> stays null.</summary>
    public static string? Persistence(CredentialPersistence? persistence) =>
        persistence is { } p ? CoreText.Get($"Persistence_{p}") : null;

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
        (null, _) => CoreText.Get("Note_New"),
        (_, null) => CoreText.Get("Note_Delete"),
        ({ } b, { } a) when b.Kind != a.Kind => $"{b.Kind.RegistryName()} -> {a.Kind.RegistryName()}",
        _ => string.Empty,
    };
}
