using System.Text;

namespace EnvKit.Core.Variables;

public sealed record Expansion(string Value, IReadOnlyList<string> Unresolved)
{
    public bool IsComplete => Unresolved.Count == 0;
}

/// <summary>
/// Expands <c>%NAME%</c> references against a lookup, leaving unknown references in place
/// as Windows does. Looked-up values are expanded in turn, with a depth limit for cycles.
/// </summary>
public static class EnvExpander
{
    private const int MaxDepth = 8;

    public static Expansion Expand(string value, Func<string, string?> lookup)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(lookup);

        var unresolved = new List<string>();
        var expanded = Expand(value, lookup, unresolved, depth: 0);
        return new Expansion(expanded, [.. unresolved.Distinct(EnvVariable.NameComparer)]);
    }

    /// <summary>Pairs <c>%</c> signs the same way <see cref="Expand(string, Func{string, string?})"/> does.</summary>
    public static bool HasReferences(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var start = value.IndexOf('%', StringComparison.Ordinal);
        while (start >= 0)
        {
            var end = value.IndexOf('%', start + 1);
            if (end < 0)
            {
                return false;
            }

            if (end > start + 1)
            {
                return true;
            }

            start = value.IndexOf('%', end + 1);
        }

        return false;
    }

    private static string Expand(string value, Func<string, string?> lookup, List<string> unresolved, int depth)
    {
        if (!value.Contains('%', StringComparison.Ordinal))
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        var position = 0;
        while (position < value.Length)
        {
            var start = value.IndexOf('%', position);
            var end = start < 0 ? -1 : value.IndexOf('%', start + 1);
            if (end < 0)
            {
                result.Append(value, position, value.Length - position);
                break;
            }

            result.Append(value, position, start - position);
            var name = value[(start + 1)..end];
            var replacement = name.Length == 0 ? null : depth < MaxDepth ? lookup(name) : null;
            if (replacement is null)
            {
                if (name.Length > 0)
                {
                    unresolved.Add(name);
                }

                result.Append(value, start, end - start + 1);
            }
            else
            {
                result.Append(Expand(replacement, lookup, unresolved, depth + 1));
            }

            position = end + 1;
        }

        return result.ToString();
    }
}
