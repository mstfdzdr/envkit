using System.Text;
using System.Text.Json.Serialization;

namespace EnvKit.Core.Credentials;

/// <summary>Mirrors CRED_TYPE_*; the numeric values are the Windows ones.</summary>
public enum CredentialKind
{
    Generic = 1,
    DomainPassword = 2,
    DomainCertificate = 3,
    DomainVisiblePassword = 4,
    GenericCertificate = 5,
    DomainExtended = 6,
}

/// <summary>Mirrors CRED_PERSIST_*.</summary>
public enum CredentialPersistence
{
    /// <summary>Gone at sign-out.</summary>
    Session = 1,

    /// <summary>This machine only.</summary>
    LocalMachine = 2,

    /// <summary>Roams with the profile.</summary>
    Enterprise = 3,
}

/// <summary>Credential Manager identifies an entry by target name and type together.</summary>
public sealed record CredentialKey(string TargetName, CredentialKind Kind)
{
    public static StringComparer TargetComparer => StringComparer.OrdinalIgnoreCase;

    public bool Equals(CredentialKey? other) =>
        other is not null && Kind == other.Kind && TargetComparer.Equals(TargetName, other.TargetName);

    public override int GetHashCode() => HashCode.Combine(Kind, TargetComparer.GetHashCode(TargetName));

    public override string ToString() => Kind == CredentialKind.Generic ? TargetName : $"{TargetName} ({Kind})";
}

/// <summary>Application-defined data stored with a credential; kept so edits do not drop it.</summary>
public sealed record CredentialAttributeEntry(string Keyword, uint Flags, byte[] Value)
{
    public bool Equals(CredentialAttributeEntry? other) =>
        other is not null && Keyword == other.Keyword && Flags == other.Flags && Value.AsSpan().SequenceEqual(other.Value);

    public override int GetHashCode() => HashCode.Combine(Keyword, Flags, Value.Length);
}

/// <summary>
/// Everything Windows stores for one credential except its identity and timestamp.
/// <see cref="Secret"/> is <see langword="null"/> for domain types, whose secret Windows never
/// hands out.
/// </summary>
public sealed record CredentialState(
    string? UserName,
    byte[]? Secret,
    CredentialPersistence Persistence,
    string? Comment = null,
    string? TargetAlias = null,
    IReadOnlyList<CredentialAttributeEntry>? Attributes = null)
{
    /// <summary>The longest secret CredWrite accepts (CRED_MAX_CREDENTIAL_BLOB_SIZE).</summary>
    public const int MaxSecretBytes = 5 * 512;

    [JsonIgnore]
    public IReadOnlyList<CredentialAttributeEntry> AttributeList => Attributes ?? [];

    /// <summary>Most tools store a UTF-16 password; some (git, Docker) store UTF-8.</summary>
    /// <remarks>Ignored by serialization: snapshots keep only the raw bytes.</remarks>
    [JsonIgnore]
    public string? SecretText => Secret is null ? null : DetectEncoding(Secret).GetString(Secret);

    /// <summary>Encodes <paramref name="text"/> the way <paramref name="previous"/> was, so editing keeps the owning tool's format.</summary>
    public static byte[] EncodeSecret(string text, byte[]? previous = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return (previous is null ? Encoding.Unicode : DetectEncoding(previous)).GetBytes(text);
    }

    /// <summary>
    /// A blob carries no encoding marker. UTF-16 text (mostly Latin) has many zero high bytes;
    /// otherwise strict UTF-8 that decodes to printable text wins, and UTF-16 is the fallback.
    /// </summary>
    public static Encoding DetectEncoding(byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (secret.Length >= 2 && secret.Length % 2 == 0)
        {
            var zeroHighBytes = 0;
            for (var i = 1; i < secret.Length; i += 2)
            {
                zeroHighBytes += secret[i] == 0 ? 1 : 0;
            }

            if (zeroHighBytes * 2 >= secret.Length / 2)
            {
                return Encoding.Unicode;
            }
        }

        try
        {
            var text = StrictUtf8.GetString(secret);
            return text.Any(char.IsControl) ? Encoding.Unicode : Encoding.UTF8;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Unicode;
        }
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public bool Equals(CredentialState? other) =>
        other is not null
        && UserName == other.UserName
        && Persistence == other.Persistence
        && Comment == other.Comment
        && TargetAlias == other.TargetAlias
        && (Secret is null ? other.Secret is null : other.Secret is not null && Secret.AsSpan().SequenceEqual(other.Secret))
        && AttributeList.SequenceEqual(other.AttributeList);

    public override int GetHashCode() => HashCode.Combine(UserName, Persistence, Comment, Secret?.Length);

    /// <summary>Never prints the secret.</summary>
    public override string ToString() => $"{UserName} ({Persistence}{(Secret is null ? ", no readable secret" : string.Empty)})";
}

public sealed record Credential(CredentialKey Key, CredentialState State, DateTimeOffset LastWritten)
{
    public string TargetName => Key.TargetName;

    public CredentialKind Kind => Key.Kind;

    /// <summary>Only generic credentials can be read back and rewritten as-is.</summary>
    public bool IsEditable => Kind == CredentialKind.Generic;

    public override string ToString() => $"{Key} {State}";
}

public interface ICredentialStore
{
    /// <param name="filter">CredEnumerate's filter, e.g. <c>git:*</c>; <see langword="null"/> for all.</param>
    IReadOnlyList<Credential> List(string? filter = null);

    Credential? Get(CredentialKey key);

    void Write(CredentialKey key, CredentialState state);

    /// <returns><see langword="false"/> if it did not exist.</returns>
    bool Delete(CredentialKey key);
}
