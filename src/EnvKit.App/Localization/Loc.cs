using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows.Data;
using System.Windows.Markup;
using EnvKit.Core;

namespace EnvKit.App.Localization;

/// <summary>
/// The app's language. Bindings go through the indexer, so changing the language updates
/// every bound text immediately; code reads <see cref="Get"/> and <see cref="Format"/>.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources = new("EnvKit.App.Resources.Strings", typeof(Loc).Assembly);

    private Loc()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>After <see cref="PropertyChanged"/>, for view models whose computed texts depend on the language.</summary>
    public event EventHandler? CultureChanged;

    public static Loc Instance { get; } = new();

    public static CultureInfo English { get; } = CultureInfo.GetCultureInfo("en");

    public static CultureInfo Turkish { get; } = CultureInfo.GetCultureInfo("tr");

    public CultureInfo Culture { get; private set; } = English;

    public string this[string key] => Resources.GetString(key, Culture) ?? key;

    public static string Get(string key) => Instance[key];

    public static string Format(string key, params object?[] args) => string.Format(Instance.Culture, Instance[key], args);

    /// <summary>Turkish when Windows is Turkish, otherwise English.</summary>
    public static CultureInfo SystemDefault() =>
        CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "tr" ? Turkish : English;

    public void SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        Culture = culture;

        // Core's texts (plan summaries, PATH problems) and WPF's own formatting follow along.
        CoreText.Culture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary><c>{l:Tr Key}</c>: a live binding to a localized text.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}

/// <summary>Shows an enum value as <c>Enum_{Type}_{Value}</c> from the resources, falling back to its name.</summary>
public sealed class EnumTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Enum e ? Text(e) : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public static string Text(Enum value)
    {
        var key = $"Enum_{value.GetType().Name}_{value}";
        var text = Loc.Get(key);
        return text == key ? value.ToString() : text;
    }
}
