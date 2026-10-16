namespace EnvKit.Core.Security;

/// <summary>Encrypts data at rest for the current user (DPAPI on Windows).</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] data);

    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The data is corrupt or was protected for another user.
    /// </exception>
    byte[] Unprotect(byte[] data);
}
