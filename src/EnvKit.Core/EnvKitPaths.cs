namespace EnvKit.Core;

public static class EnvKitPaths
{
    /// <summary>
    /// Kept apart from the install folder (<c>%LOCALAPPDATA%\EnvKit</c>), which the installer
    /// deletes on uninstall: the snapshots should outlive the app.
    /// </summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EnvKit.Data");

    public static string Snapshots { get; } = Path.Combine(DataDirectory, "snapshots");

    public static string Plans { get; } = Path.Combine(DataDirectory, "plans");
}
