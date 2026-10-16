using Velopack;
using Velopack.Sources;

namespace EnvKit.App;

public interface IAppUpdater
{
    /// <summary><see langword="false"/> when running from a build folder rather than an installation.</summary>
    bool IsInstalled { get; }

    /// <summary>Downloads the newest release, if there is one.</summary>
    /// <returns>Its version, or <see langword="null"/> when up to date or not installed.</returns>
    Task<string?> DownloadUpdateAsync();

    /// <summary>Restarts into the downloaded release.</summary>
    void ApplyAndRestart();
}

/// <summary>Updates from GitHub Releases, where the Release workflow publishes.</summary>
public sealed class VelopackUpdater : IAppUpdater
{
    public const string RepositoryUrl = "https://github.com/mstfdzdr/envkit";

    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, null, false, null));
    private UpdateInfo? _downloaded;

    public bool IsInstalled => _manager.IsInstalled;

    public async Task<string?> DownloadUpdateAsync()
    {
        // Running from a build folder rather than an installation: nothing to update.
        if (!_manager.IsInstalled)
        {
            return null;
        }

        var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        if (update is null)
        {
            return null;
        }

        await _manager.DownloadUpdatesAsync(update, null, CancellationToken.None).ConfigureAwait(false);
        _downloaded = update;
        return update.TargetFullRelease.Version.ToString();
    }

    public void ApplyAndRestart()
    {
        if (_downloaded is not null)
        {
            _manager.ApplyUpdatesAndRestart(_downloaded.TargetFullRelease, null);
        }
    }
}
