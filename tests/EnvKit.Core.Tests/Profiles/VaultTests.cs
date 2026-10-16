using System.Security.Cryptography;
using System.Text;
using EnvKit.Core.Security;
using EnvKit.Core.Vault;
using EnvKit.Testing;

namespace EnvKit.Core.Tests.Profiles;

public sealed class VaultTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "EnvKit.Tests", Guid.NewGuid().ToString("N"), "vault.dat");

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void File_vault_round_trips_and_is_not_plain_text()
    {
        var vault = new FileVault(_path, new XorProtector());

        vault.Set("openai", "sk-secret");
        vault.Set("GitHub.Token", "ghp_x");

        var reopened = new FileVault(_path, new XorProtector());
        Assert.Equal(["GitHub.Token", "openai"], reopened.Names());
        Assert.Equal("sk-secret", reopened.Get("OPENAI"));
        Assert.DoesNotContain("sk-secret", Encoding.UTF8.GetString(File.ReadAllBytes(_path)), StringComparison.Ordinal);
    }

    [Fact]
    public void Deleting_reports_whether_the_secret_existed()
    {
        var vault = new FileVault(_path, new XorProtector());
        vault.Set("a", "1");

        Assert.True(vault.Delete("a"));
        Assert.False(vault.Delete("a"));
        Assert.Empty(vault.Names());
    }

    [Fact]
    public void Missing_file_is_an_empty_vault() => Assert.Empty(new FileVault(_path, new XorProtector()).Names());

    [Fact]
    public void Undecryptable_vault_fails_instead_of_being_overwritten()
    {
        new FileVault(_path, new XorProtector()).Set("a", "1");
        var other = new FileVault(_path, new FailingProtector());

        Assert.Throws<CryptographicException>(() => other.Set("b", "2"));
        Assert.Equal("1", new FileVault(_path, new XorProtector()).Get("a"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("../escape")]
    [InlineData("x:y")]
    public void Rejects_invalid_names(string name) =>
        Assert.Throws<ArgumentException>(() => new InMemoryVault().Set(name, "v"));

    [Fact]
    public void References_resolve_and_report_missing_secrets()
    {
        var vault = new InMemoryVault();
        vault.Set("token", "abc%PATH%");

        Assert.True(VaultReference.HasReferences("Bearer ${vault:token}"));
        Assert.Equal("Bearer abc%PATH%", VaultReference.Resolve("Bearer ${vault:token}", vault));
        Assert.Equal(["token", "other"], VaultReference.Names("${vault:token}${vault:other}${vault:TOKEN}"));

        var missing = Assert.Throws<KeyNotFoundException>(() => VaultReference.Resolve("${vault:other}", vault));
        Assert.Contains("other", missing.Message, StringComparison.Ordinal);
    }

    private sealed class FailingProtector : ISecretProtector
    {
        public byte[] Protect(byte[] data) => data;

        public byte[] Unprotect(byte[] data) => throw new CryptographicException("Key not valid for this user.");
    }
}
