using EnvKit.Core.Credentials;

namespace EnvKit.Testing;

public sealed class InMemoryCredentialStore : ICredentialStore
{
    private readonly Dictionary<CredentialKey, Credential> _items = [];

    /// <summary>Writes to these keys throw, to exercise rollback.</summary>
    public HashSet<CredentialKey> FailOnWrite { get; } = [];

    public void Seed(CredentialKey key, CredentialState state) => _items[key] = new Credential(key, state, DateTimeOffset.UnixEpoch);

    public IReadOnlyList<Credential> List(string? filter = null) =>
        [.. _items.Values.Where(c => filter is null || Matches(c.TargetName, filter))];

    public Credential? Get(CredentialKey key) => _items.GetValueOrDefault(key);

    public void Write(CredentialKey key, CredentialState state)
    {
        if (FailOnWrite.Contains(key))
        {
            throw new InvalidOperationException($"Simulated failure writing {key}.");
        }

        _items[key] = new Credential(key, state, DateTimeOffset.UtcNow);
    }

    public bool Delete(CredentialKey key) => _items.Remove(key);

    // CredEnumerate filters support a trailing '*' only.
    private static bool Matches(string target, string filter) =>
        filter.EndsWith('*')
            ? target.StartsWith(filter[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(target, filter, StringComparison.OrdinalIgnoreCase);
}
