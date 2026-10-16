using EnvKit.Core.Profiles;
using EnvKit.Core.Vault;

namespace EnvKit.Testing;

public sealed class InMemoryVault : IVault
{
    private readonly Dictionary<string, string> _secrets = new(VaultNames.Comparer);

    public IReadOnlyList<string> Names() => [.. _secrets.Keys.Order(VaultNames.Comparer)];

    public string? Get(string name) => _secrets.GetValueOrDefault(name);

    public void Set(string name, string value)
    {
        VaultNames.Validate(name);
        _secrets[name] = value;
    }

    public bool Delete(string name) => _secrets.Remove(name);
}

public sealed class InMemoryProfileStore : IProfileStore
{
    private readonly Dictionary<string, Profile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Profile> List() => [.. _profiles.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];

    public Profile? Get(string name) => _profiles.GetValueOrDefault(name);

    public void Save(Profile profile)
    {
        profile.Validate();
        _profiles[profile.Name] = profile;
    }

    public bool Delete(string name) => _profiles.Remove(name);
}
