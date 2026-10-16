using System.Security.Cryptography;
using EnvKit.Core.Security;

namespace EnvKit.Windows.Security;

/// <summary>DPAPI, current-user scope: only this Windows account can decrypt.</summary>
public sealed class DpapiProtector : ISecretProtector
{
    // Ties the ciphertext to EnvKit, so other software running as the user cannot
    // unprotect it with a bare CryptUnprotectData call.
    private static readonly byte[] Entropy = "EnvKit.v1"u8.ToArray();

    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}
