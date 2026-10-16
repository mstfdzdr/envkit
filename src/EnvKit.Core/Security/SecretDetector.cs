namespace EnvKit.Core.Security;

/// <summary>
/// Guesses whether a variable holds a credential, so views can mask it until asked.
/// Errs towards masking: hiding a harmless value costs a click, showing a token can leak it.
/// </summary>
public static class SecretDetector
{
    public const string Mask = "••••••••";

    /// <summary>For consoles whose code page lacks <see cref="Mask"/>'s bullets.</summary>
    public const string AsciiMask = "********";

    // Matched against the name with separators removed, so API_KEY, ApiKey and api-key all count.
    private static readonly string[] NameFragments =
    [
        "TOKEN", "SECRET", "PASSWORD", "PASSWD", "PWD", "APIKEY", "ACCESSKEY",
        "PRIVATEKEY", "CREDENTIAL", "CONNECTIONSTRING", "CONNSTR",
    ];

    private static readonly string[] NameSuffixes = ["_KEY", "_PAT", "_PASS", "_AUTH"];

    // Well-known token formats (GitHub, OpenAI-style, Slack, AWS, Azure DevOps-style, npm, GitLab).
    private static readonly string[] ValuePrefixes =
    [
        "ghp_", "gho_", "ghs_", "github_pat_", "sk-", "xoxb-", "xoxp-", "AKIA", "npm_", "glpat-",
    ];

    public static bool LooksSecret(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        var upper = name.ToUpperInvariant();
        var compact = upper.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        return upper is "PAT" or "KEY"
            || NameFragments.Any(f => compact.Contains(f, StringComparison.Ordinal))
            || NameSuffixes.Any(s => upper.EndsWith(s, StringComparison.Ordinal))
            || ValuePrefixes.Any(p => value.StartsWith(p, StringComparison.Ordinal));
    }
}
