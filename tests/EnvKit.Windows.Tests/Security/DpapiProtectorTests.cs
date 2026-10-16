using System.Security.Cryptography;
using EnvKit.Windows.Security;

namespace EnvKit.Windows.Tests.Security;

public class DpapiProtectorTests
{
    private readonly DpapiProtector _protector = new();

    [Fact]
    public void Round_trips_and_does_not_leak_plaintext()
    {
        var data = "ghp_secret"u8.ToArray();

        var protectedData = _protector.Protect(data);

        Assert.NotEqual(data, protectedData);
        Assert.Equal(data, _protector.Unprotect(protectedData));
    }

    [Fact]
    public void Data_protected_without_EnvKit_entropy_is_rejected()
    {
        var foreign = ProtectedData.Protect("x"u8.ToArray(), null, DataProtectionScope.CurrentUser);

        Assert.ThrowsAny<CryptographicException>(() => _protector.Unprotect(foreign));
    }
}
