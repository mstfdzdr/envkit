using System.Globalization;
using System.Resources;
using EnvKit.Core.Variables;

namespace EnvKit.Core;

/// <summary>
/// Localized text produced by Core (plan summaries, PATH problems). The culture is explicit
/// rather than the thread's: the CLI and tests pin English, the app follows its language setting.
/// </summary>
public static class CoreText
{
    private static readonly ResourceManager Resources = new("EnvKit.Core.Resources.CoreStrings", typeof(CoreText).Assembly);

    /// <summary><see langword="null"/> follows <see cref="CultureInfo.CurrentUICulture"/>.</summary>
    public static CultureInfo? Culture { get; set; }

    private static CultureInfo Current => Culture ?? CultureInfo.CurrentUICulture;

    public static string Get(string key) => Lookup(Current, key);

    public static string Format(string key, params object?[] args) => FormatIn(null, key, args);

    /// <summary><see cref="Format"/> in a given language; <see langword="null"/> uses <see cref="Culture"/>.</summary>
    public static string FormatIn(CultureInfo? culture, string key, params object?[] args)
    {
        var resolved = culture ?? Current;
        return string.Format(resolved, Lookup(resolved, key), args);
    }

    private static string Lookup(CultureInfo culture, string key) => Resources.GetString(key, culture) ?? key;

    public static string Scope(EnvScope scope) => Get($"Scope_{scope}");
}
