namespace EnvKit.Core.Paths;

public static class PathList
{
    /// <summary>
    /// Splits on <c>;</c> outside double quotes (<c>"C:\a;b"</c> is one directory).
    /// Empty segments are kept so that <see cref="Join"/> reproduces the input exactly.
    /// </summary>
    public static IReadOnlyList<string> Split(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
        {
            return [];
        }

        var entries = new List<string>();
        var start = 0;
        var quoted = false;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '"')
            {
                quoted = !quoted;
            }
            else if (value[i] == ';' && !quoted)
            {
                entries.Add(value[start..i]);
                start = i + 1;
            }
        }

        entries.Add(value[start..]);
        return entries;
    }

    public static string Join(IEnumerable<string> entries) => string.Join(';', entries);

    /// <summary>
    /// Comparison form of a directory: quotes removed, <c>/</c> as <c>\</c>, no trailing
    /// separator except on a drive root. Compare results case-insensitively.
    /// </summary>
    public static string Normalize(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        var normalized = directory.Replace("\"", string.Empty, StringComparison.Ordinal).Trim().Replace('/', '\\');
        while (normalized.Length > 1 && normalized[^1] == '\\' && !IsDriveRoot(normalized))
        {
            normalized = normalized[..^1];
        }

        return normalized;
    }

    /// <summary>
    /// <c>C:\x</c> or <c>\\server\share</c>. Anything else (<c>bin</c>, <c>C:x</c>, <c>\x</c>)
    /// depends on the current directory or drive.
    /// </summary>
    public static bool IsFullyQualified(string normalized) =>
        normalized.StartsWith(@"\\", StringComparison.Ordinal)
        || (normalized.Length >= 3 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':' && normalized[2] == '\\');

    private static bool IsDriveRoot(string path) => path.Length == 3 && path[1] == ':';
}
