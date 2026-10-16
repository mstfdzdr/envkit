namespace EnvKit.Core.Variables;

/// <summary>
/// Tells running processes (Explorer, terminals that listen) that persistent variables changed.
/// Processes that are already running and do not listen keep their old environment.
/// </summary>
public interface IEnvChangeBroadcaster
{
    /// <returns><see langword="false"/> if the broadcast could not be sent.</returns>
    bool Broadcast();
}
