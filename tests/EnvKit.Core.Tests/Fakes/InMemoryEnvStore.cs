using EnvKit.Core.Variables;

namespace EnvKit.Core.Tests.Fakes;

internal sealed class InMemoryEnvStore : IEnvStore
{
    private readonly Dictionary<EnvScope, Dictionary<string, EnvVariable>> _scopes = new()
    {
        [EnvScope.User] = new(EnvVariable.NameComparer),
        [EnvScope.Machine] = new(EnvVariable.NameComparer),
    };

    public IReadOnlyList<EnvVariable> GetAll(EnvScope scope) => [.. _scopes[scope].Values];

    public EnvVariable? Get(string name, EnvScope scope) => _scopes[scope].GetValueOrDefault(name);

    public void Set(EnvVariable variable) => _scopes[variable.Scope][variable.Name] = variable;

    public bool Delete(string name, EnvScope scope) => _scopes[scope].Remove(name);
}
