using System.Text;
using System.Text.RegularExpressions;

namespace EnvKit.Core.DotEnv;

public enum DotEnvQuote
{
    None,

    /// <summary>'...': taken literally.</summary>
    Literal,

    /// <summary>"...": backslash escapes, may span lines.</summary>
    Escaped,
}

/// <summary>
/// One physical piece of a .env file. Assignments are split into their parts so an edit can
/// replace just the key or the value; every other byte of the line is written back as read.
/// </summary>
public sealed class DotEnvLine
{
    internal DotEnvLine(string raw, int lineNumber)
    {
        Text = raw;
        LineNumber = lineNumber;
    }

    internal DotEnvLine(int lineNumber, string indent, string export, string key, string assign, string valueRaw, string suffix, string newLine)
    {
        LineNumber = lineNumber;
        Indent = indent;
        Export = export;
        Key = key;
        Assign = assign;
        ValueRaw = valueRaw;
        Suffix = suffix;
        NewLine = newLine;
        Text = string.Empty;
    }

    /// <summary>1-based line where it starts.</summary>
    public int LineNumber { get; }

    /// <summary>Set for assignments; <see langword="null"/> for blank, comment and unparsable lines.</summary>
    public string? Key { get; private set; }

    public bool IsAssignment => Key is not null;

    /// <summary>A non-blank, non-comment line that is not a valid assignment.</summary>
    public bool IsInvalid => Key is null && Text.Trim().Length > 0 && !Text.TrimStart().StartsWith('#');

    public DotEnvQuote Quote => ValueRaw.Length >= 2 && ValueRaw[0] == '"' ? DotEnvQuote.Escaped
        : ValueRaw.Length >= 2 && ValueRaw[0] == '\'' ? DotEnvQuote.Literal
        : DotEnvQuote.None;

    public string Value => Quote switch
    {
        DotEnvQuote.Escaped => Unescape(ValueRaw[1..^1]),
        DotEnvQuote.Literal => ValueRaw[1..^1],
        _ => ValueRaw,
    };

    internal string Text { get; private set; }

    internal string Indent { get; } = string.Empty;

    internal string Export { get; } = string.Empty;

    internal string Assign { get; } = string.Empty;

    internal string ValueRaw { get; private set; } = string.Empty;

    internal string Suffix { get; } = string.Empty;

    internal string NewLine { get; set; } = string.Empty;

    internal string Raw => IsAssignment ? Indent + Export + Key + Assign + ValueRaw + Suffix + NewLine : Text;

    internal bool EndsWithNewLine => IsAssignment ? NewLine.Length > 0 : Text.EndsWith('\n');

    internal void EnsureNewLine(string newLine)
    {
        if (EndsWithNewLine)
        {
            return;
        }

        if (IsAssignment)
        {
            NewLine = newLine;
        }
        else
        {
            Text += newLine;
        }
    }

    internal void SetValue(string value) => ValueRaw = DotEnvDocument.Encode(value, Quote);

    internal void Rename(string key) => Key = key;

    private static string Unescape(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                i++;
                result.Append(text[i] switch
                {
                    'n' => "\n",
                    'r' => "\r",
                    't' => "\t",
                    '"' => "\"",
                    '\\' => "\\",
                    var other => $"\\{other}",
                });
            }
            else
            {
                result.Append(text[i]);
            }
        }

        return result.ToString();
    }
}

/// <summary>
/// A .env file that round-trips exactly: <see cref="ToText"/> reproduces the input, and edits
/// rewrite only the lines they touch. Keys are case-sensitive, and the last assignment of a
/// key wins, as in the common dotenv loaders.
/// </summary>
public sealed partial class DotEnvDocument
{
    private readonly List<DotEnvLine> _lines;

    private DotEnvDocument(List<DotEnvLine> lines, string newLine)
    {
        _lines = lines;
        NewLine = newLine;
    }

    /// <summary>The file's line ending, used for added lines.</summary>
    public string NewLine { get; }

    public IReadOnlyList<DotEnvLine> Lines => _lines;

    public IEnumerable<DotEnvLine> Entries => _lines.Where(l => l.IsAssignment);

    /// <summary>The effective value of each key (last assignment wins), in file order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Values()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in Entries)
        {
            result[entry.Key!] = entry.Value;
        }

        return [.. result];
    }

    public IEnumerable<string> DuplicateKeys => Entries.GroupBy(e => e.Key!, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key);

    public IEnumerable<DotEnvLine> InvalidLines => _lines.Where(l => l.IsInvalid);

    public static bool IsValidKey(string? key) => key is not null && WholeKeyPattern().IsMatch(key);

    public string? Get(string key) => Entries.LastOrDefault(e => e.Key == key)?.Value;

    /// <summary>Changes the last assignment of <paramref name="key"/> in place, or appends one.</summary>
    public void Set(string key, string value)
    {
        if (!IsValidKey(key))
        {
            throw new ArgumentException($"'{key}' is not a valid .env key: start with a letter or '_', then letters, digits, '_', '.' or '-'.", nameof(key));
        }

        ArgumentNullException.ThrowIfNull(value);
        if (Entries.LastOrDefault(e => e.Key == key) is { } existing)
        {
            existing.SetValue(value);
            return;
        }

        // Keep the file's choice about a trailing newline: the old last line gets one, the new one inherits its absence.
        var trailing = NewLine;
        if (_lines.Count > 0 && !_lines[^1].EndsWithNewLine)
        {
            trailing = string.Empty;
            _lines[^1].EnsureNewLine(NewLine);
        }

        _lines.Add(new DotEnvLine(_lines.Count + 1, string.Empty, string.Empty, key, "=", Encode(value, DotEnvQuote.None), string.Empty, trailing));
    }

    /// <returns>How many assignments were removed.</returns>
    public int Remove(string key) => _lines.RemoveAll(l => l.Key == key);

    /// <exception cref="ArgumentException"><paramref name="newKey"/> is invalid or already used.</exception>
    public int Rename(string oldKey, string newKey)
    {
        if (!IsValidKey(newKey))
        {
            throw new ArgumentException($"'{newKey}' is not a valid .env key.", nameof(newKey));
        }

        if (oldKey != newKey && Entries.Any(e => e.Key == newKey))
        {
            throw new ArgumentException($"'{newKey}' is already defined.", nameof(newKey));
        }

        var renamed = 0;
        foreach (var entry in Entries.Where(e => e.Key == oldKey).ToList())
        {
            entry.Rename(newKey);
            renamed++;
        }

        return renamed;
    }

    public string ToText() => string.Concat(_lines.Select(l => l.Raw));

    /// <summary>Keeps the existing quoting when it can represent the value; otherwise picks the simplest that can.</summary>
    internal static string Encode(string value, DotEnvQuote preferred)
    {
        var safeUnquoted = value.Length > 0
            && !value.Contains('\n') && !value.Contains('\r')
            && value.Trim() == value
            && value[0] is not ('"' or '\'')
            && !value.Contains(" #", StringComparison.Ordinal) && !value.Contains("\t#", StringComparison.Ordinal);
        var safeSingle = !value.Contains('\'') && !value.Contains('\n') && !value.Contains('\r');

        return preferred switch
        {
            DotEnvQuote.None when safeUnquoted || value.Length == 0 => value,
            DotEnvQuote.Literal when safeSingle => $"'{value}'",
            _ => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}\"",
        };
    }

    public static DotEnvDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<DotEnvLine>();
        var position = 0;
        var lineNumber = 1;
        while (position < text.Length)
        {
            var (line, next) = ParseLine(text, position, lineNumber);
            lines.Add(line);
            lineNumber += CountNewLines(text, position, next);
            position = next;
        }

        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\n') ? "\n" : Environment.NewLine;
        return new DotEnvDocument(lines, newLine);
    }

    private static (DotEnvLine Line, int Next) ParseLine(string text, int start, int lineNumber)
    {
        var lineEnd = text.IndexOf('\n', start);
        var physicalEnd = lineEnd < 0 ? text.Length : lineEnd + 1;
        var invalid = (new DotEnvLine(text[start..physicalEnd], lineNumber), physicalEnd);

        var i = start;
        while (i < text.Length && text[i] is ' ' or '\t')
        {
            i++;
        }

        var indent = text[start..i];
        var export = string.Empty;
        var exportMatch = ExportPattern().Match(text, i);
        if (exportMatch.Success && exportMatch.Index == i)
        {
            export = exportMatch.Value;
            i += export.Length;
        }

        var keyMatch = KeyPattern().Match(text, i);
        if (!keyMatch.Success || keyMatch.Index != i)
        {
            return invalid;
        }

        var key = keyMatch.Value;
        var assignStart = i + key.Length;
        i = assignStart;
        while (i < text.Length && text[i] is ' ' or '\t')
        {
            i++;
        }

        if (i >= text.Length || text[i] != '=')
        {
            return invalid;
        }

        i++;
        while (i < text.Length && text[i] is ' ' or '\t')
        {
            i++;
        }

        var assign = text[assignStart..i];
        var valueStart = i;
        int valueEnd;
        if (i < text.Length && text[i] is '"' or '\'')
        {
            var quote = text[i];
            var j = i + 1;
            while (j < text.Length && text[j] != quote)
            {
                j += quote == '"' && text[j] == '\\' ? 2 : 1;
            }

            if (j >= text.Length)
            {
                // An unterminated quote: keep the line as it is rather than swallowing the rest of the file.
                return invalid;
            }

            valueEnd = j + 1;
            lineEnd = text.IndexOf('\n', valueEnd);
            physicalEnd = lineEnd < 0 ? text.Length : lineEnd + 1;
        }
        else
        {
            var contentEnd = lineEnd < 0 ? text.Length : (lineEnd > start && text[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd);

            // An unquoted value ends at a '#' that follows whitespace (an inline comment) or at the line end.
            valueEnd = contentEnd;
            for (var k = valueStart; k < contentEnd; k++)
            {
                // text[k - 1] is always inside the assignment: '=' or whitespace at the latest.
                if (text[k] == '#' && text[k - 1] is ' ' or '\t')
                {
                    valueEnd = k;
                    break;
                }
            }

            while (valueEnd > valueStart && text[valueEnd - 1] is ' ' or '\t')
            {
                valueEnd--;
            }
        }

        var newLineStart = lineEnd < 0 ? text.Length : (lineEnd > valueEnd && text[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd);
        var suffix = text[valueEnd..Math.Max(valueEnd, newLineStart)];
        var newLine = text[Math.Max(valueEnd, newLineStart)..physicalEnd];
        return (new DotEnvLine(lineNumber, indent, export, key, assign, text[valueStart..valueEnd], suffix, newLine), physicalEnd);
    }



    private static int CountNewLines(string text, int from, int to)
    {
        var count = 0;
        for (var i = from; i < to; i++)
        {
            count += text[i] == '\n' ? 1 : 0;
        }

        return count;
    }

    [GeneratedRegex(@"\Gexport[ \t]+")]
    private static partial Regex ExportPattern();

    [GeneratedRegex(@"\G[A-Za-z_][A-Za-z0-9_.\-]*")]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.\-]*$")]
    private static partial Regex WholeKeyPattern();
}
