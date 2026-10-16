using System.Globalization;
using System.Runtime.CompilerServices;

namespace EnvKit.Core.Tests;

internal static class EnglishText
{
    /// <summary>Assertions use the English texts, whatever language the machine running the tests uses.</summary>
    [ModuleInitializer]
    internal static void Pin() => CoreText.Culture = CultureInfo.InvariantCulture;
}
