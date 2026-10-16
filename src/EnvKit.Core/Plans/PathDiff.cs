using EnvKit.Core.Paths;

namespace EnvKit.Core.Plans;

public enum DiffKind
{
    Unchanged,
    Added,
    Removed,
}

public sealed record DiffLine(DiffKind Kind, string Text);

/// <summary>Entry-level diff of two PATH values, so a reorder or one addition reads as such.</summary>
public static class PathDiff
{
    public static IReadOnlyList<DiffLine> Compute(string? before, string? after)
    {
        var a = before is null ? [] : PathList.Split(before);
        var b = after is null ? [] : PathList.Split(after);

        // Longest common subsequence table, filled from the end.
        var lcs = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
        {
            for (var j = b.Count - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var lines = new List<DiffLine>();
        int x = 0, y = 0;
        while (x < a.Count && y < b.Count)
        {
            if (a[x] == b[y])
            {
                lines.Add(new DiffLine(DiffKind.Unchanged, a[x++]));
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                lines.Add(new DiffLine(DiffKind.Removed, a[x++]));
            }
            else
            {
                lines.Add(new DiffLine(DiffKind.Added, b[y++]));
            }
        }

        lines.AddRange(a.Skip(x).Select(e => new DiffLine(DiffKind.Removed, e)));
        lines.AddRange(b.Skip(y).Select(e => new DiffLine(DiffKind.Added, e)));
        return lines;
    }
}
