namespace EnvKit.Core.Variables;

/// <summary>
/// Resolves references the way a fresh logon would, per scope. Windows expands machine
/// variables before user variables exist, so a machine value cannot see user-only variables.
/// </summary>
public sealed class EnvLookup
{
    private readonly Dictionary<string, string> _machine;
    private readonly Dictionary<string, string> _user;
    private readonly Func<string, string?> _system;

    /// <param name="systemFallback">
    /// Values Windows supplies outside the Environment keys (USERPROFILE, SystemRoot, APPDATA, …);
    /// usually the current process environment.
    /// </param>
    public EnvLookup(IEnvStore store, Func<string, string?> systemFallback)
    {
        ArgumentNullException.ThrowIfNull(store);
        _machine = ToDictionary(store.GetAll(EnvScope.Machine));
        _user = ToDictionary(store.GetAll(EnvScope.User));
        _system = systemFallback;
    }

    public static EnvLookup FromProcess(IEnvStore store) => new(store, Environment.GetEnvironmentVariable);

    public string? Resolve(string name, EnvScope scope) => scope switch
    {
        // The process fallback already contains user variables; hide those from machine lookups.
        EnvScope.Machine => _machine.GetValueOrDefault(name) ?? (_user.ContainsKey(name) ? null : _system(name)),
        EnvScope.User => _user.GetValueOrDefault(name) ?? _machine.GetValueOrDefault(name) ?? _system(name),
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };

    public Expansion Expand(string value, EnvScope scope) => EnvExpander.Expand(value, name => Resolve(name, scope));

    private static Dictionary<string, string> ToDictionary(IEnumerable<EnvVariable> variables) =>
        variables.ToDictionary(v => v.Name, v => v.Value, EnvVariable.NameComparer);
}
