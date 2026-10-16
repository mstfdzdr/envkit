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

    public static string Get(string key) => Resources.GetString(key, Current) ?? key;

    public static string Format(string key, params object?[] args) => string.Format(Current, Get(key), args);

    public static string Scope(EnvScope scope) => Get($"Scope_{scope}");
}
