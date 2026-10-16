using System.Runtime.CompilerServices;
using EnvKit.App.Localization;

// Language is process-wide state (Loc.Instance); tests that switch it must not overlap with others.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace EnvKit.App.Tests;

internal static class EnglishText
{
    /// <summary>Assertions use the English texts, whatever language the machine running the tests uses.</summary>
    [ModuleInitializer]
    internal static void Pin() => Loc.Instance.SetCulture(Loc.English);
}
