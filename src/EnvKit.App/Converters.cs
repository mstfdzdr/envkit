using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using EnvKit.Core.Plans;

namespace EnvKit.App;

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class DiffLineBrushConverter : IValueConverter
{
    // Readable on both the light and the dark theme.
    private static readonly Brush Added = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43)));
    private static readonly Brush Removed = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DiffKind.Added => Added,
        DiffKind.Removed => Removed,
        _ => DependencyProperty.UnsetValue,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}

public sealed class DiffLineTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DiffLine line
            ? $"{(line.Kind == DiffKind.Added ? '+' : '-')} {(line.Text.Trim().Length == 0 ? "(empty)" : line.Text)}"
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
