namespace EnvKit.Core.Variables;

/// <summary>
/// Persistent (registry-backed) environment variables. Implementations must preserve
/// <see cref="EnvVariable.Kind"/> on round-trip and compare names case-insensitively.
/// Writes do not notify other processes; that is the caller's job after a plan is applied.
/// </summary>
public interface IEnvStore
{
    IReadOnlyList<EnvVariable> GetAll(EnvScope scope);

    EnvVariable? Get(string name, EnvScope scope);

    void Set(EnvVariable variable);

    /// <returns><see langword="false"/> if the variable did not exist.</returns>
    bool Delete(string name, EnvScope scope);
}
