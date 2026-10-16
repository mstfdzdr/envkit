using System.Text;
using EnvKit.Core.Credentials;

namespace EnvKit.Core.Tests.Credentials;

public class CredentialTests
{
    [Theory]
    [InlineData("abcd")]
    [InlineData("ghp_0123456789abcdefABCDEF")]
    [InlineData("şifre-ĞÜİ")]
    [InlineData("x")]
    public void Secrets_decode_in_either_encoding(string password)
    {
        foreach (var encoding in new Encoding[] { Encoding.Unicode, Encoding.UTF8 })
        {
            var state = new CredentialState("me", encoding.GetBytes(password), CredentialPersistence.LocalMachine);

            Assert.Equal(password, state.SecretText);
        }
    }

    [Fact]
    public void Re_encoding_keeps_the_previous_format()
    {
        var utf8 = Encoding.UTF8.GetBytes("old-token");

        Assert.Equal(Encoding.UTF8.GetBytes("new"), CredentialState.EncodeSecret("new", utf8));
        Assert.Equal(Encoding.Unicode.GetBytes("new"), CredentialState.EncodeSecret("new"));
    }

    [Fact]
    public void States_compare_by_value_including_secret_bytes()
    {
        var a = new CredentialState("me", [1, 2], CredentialPersistence.LocalMachine, Attributes: [new("k", 0, [9])]);
        var b = new CredentialState("me", [1, 2], CredentialPersistence.LocalMachine, Attributes: [new("k", 0, [9])]);
        var c = a with { Secret = [1, 3] };

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(a, a with { Secret = null });
    }

    [Fact]
    public void ToString_never_contains_the_secret()
    {
        var state = new CredentialState("me", Encoding.Unicode.GetBytes("hunter2"), CredentialPersistence.LocalMachine);

        Assert.DoesNotContain("hunter2", state.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", new Credential(new("t", CredentialKind.Generic), state, default).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Keys_compare_target_case_insensitively_but_kind_exactly()
    {
        Assert.Equal(new CredentialKey("git:https://GitHub.com", CredentialKind.Generic), new CredentialKey("git:https://github.com", CredentialKind.Generic));
        Assert.NotEqual(new CredentialKey("srv", CredentialKind.Generic), new CredentialKey("srv", CredentialKind.DomainPassword));
    }

    [Theory]
    [InlineData("git:https://github.com", CredentialKind.Generic, CredentialGroup.Git)]
    [InlineData("GitHub - https://api.github.com/me", CredentialKind.Generic, CredentialGroup.GitHubDesktop)]
    [InlineData("vscodevscode.github-authentication/github.auth", CredentialKind.Generic, CredentialGroup.VSCode)]
    [InlineData("VSCredentials_me", CredentialKind.Generic, CredentialGroup.VisualStudio)]
    [InlineData("https://index.docker.io/v1/docker", CredentialKind.Generic, CredentialGroup.Docker)]
    [InlineData("TERMSRV/build01", CredentialKind.DomainPassword, CredentialGroup.RemoteDesktop)]
    [InlineData("MicrosoftAccount:user=me@example.com", CredentialKind.Generic, CredentialGroup.MicrosoftAccount)]
    [InlineData("MicrosoftOffice16_Data:orgid:me", CredentialKind.Generic, CredentialGroup.Office)]
    [InlineData("fileserver", CredentialKind.DomainPassword, CredentialGroup.Network)]
    [InlineData("my-app/token", CredentialKind.Generic, CredentialGroup.Other)]
    public void Classifies_by_owning_tool(string target, CredentialKind kind, CredentialGroup expected) =>
        Assert.Equal(expected, CredentialClassifier.Classify(new CredentialKey(target, kind)));

    [Fact]
    public void Export_has_no_secret()
    {
        var credential = new Credential(
            new("my-app/token", CredentialKind.Generic),
            new CredentialState("me", Encoding.Unicode.GetBytes("hunter2"), CredentialPersistence.LocalMachine),
            DateTimeOffset.UnixEpoch);

        var json = CredentialExport.ToJson([credential]);

        Assert.Contains("my-app/token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", json, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.Unicode.GetBytes("hunter2")), json, StringComparison.Ordinal);
    }
}
