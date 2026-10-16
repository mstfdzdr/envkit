using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using EnvKit.Core.Security;

namespace EnvKit.Core.Vault;

/// <summary>Named secrets that EnvKit injects into processes and never writes to the persistent environment.</summary>
public interface IVault
{
    IReadOnlyList<string> Names();

    string? Get(string name);

    void Set(string name, string value);

    /// <returns><see langword="false"/> if there was no such secret.</returns>
    bool Delete(string name);
}

public static partial class VaultNames
{
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    public static bool IsValid(string? name) => name is not null && Pattern().IsMatch(name);

    public static void Validate(string name)
    {
        if (!IsValid(name))
        {
            throw new ArgumentException($"'{name}' is not a valid name: use 1-128 letters, digits, '.', '_' or '-'.", nameof(name));
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,128}$")]
    private static partial Regex Pattern();
}

/// <summary><c>${vault:NAME}</c> inside a value stands for the secret <c>NAME</c>.</summary>
public static partial class VaultReference
{
    public static string For(string name) => $"${{vault:{name}}}";

    public static bool HasReferences(string value) => Pattern().IsMatch(value);

    public static IReadOnlyList<string> Names(string value) =>
        [.. Pattern().Matches(value).Select(m => m.Groups[1].Value).Distinct(VaultNames.Comparer)];

    /// <exception cref="KeyNotFoundException">A referenced secret is not in the vault.</exception>
    public static string Resolve(string value, IVault vault)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(vault);
        var missing = Names(value).Where(n => vault.Get(n) is null).ToList();
        if (missing.Count > 0)
        {
            throw new KeyNotFoundException($"Not in the vault: {string.Join(", ", missing)}.");
        }

        return Pattern().Replace(value, m => vault.Get(m.Groups[1].Value)!);
    }

    [GeneratedRegex(@"\$\{vault:([A-Za-z0-9_.-]{1,128})\}")]
    private static partial Regex Pattern();
}

/// <summary>One DPAPI-protected file holding every secret.</summary>
public sealed class FileVault(string path, ISecretProtector protector) : IVault
{
    public static string DefaultPath => EnvKitPaths.Vault;

    public IReadOnlyList<string> Names() => [.. Load().Keys.Order(VaultNames.Comparer)];

    public string? Get(string name) => Load().GetValueOrDefault(name);

    public void Set(string name, string value)
    {
        VaultNames.Validate(name);
        ArgumentNullException.ThrowIfNull(value);
        var secrets = Load();
        secrets[name] = value;
        Save(secrets);
    }

    public bool Delete(string name)
    {
        var secrets = Load();
        if (!secrets.Remove(name))
        {
            return false;
        }

        Save(secrets);
        return true;
    }

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(path))
        {
            return new(VaultNames.Comparer);
        }

        // A vault that cannot be decrypted (another user, a copied profile) must fail loudly:
        // treating it as empty would let the next Set overwrite every secret.
        var json = protector.Unprotect(File.ReadAllBytes(path));
        try
        {
            var secrets = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? throw new JsonException("Empty vault.");
            return new(secrets, VaultNames.Comparer);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }

    private void Save(Dictionary<string, string> secrets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(secrets);
        try
        {
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, protector.Protect(plain));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}
