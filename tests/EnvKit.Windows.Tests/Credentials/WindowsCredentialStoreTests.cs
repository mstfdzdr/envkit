using System.Text;
using EnvKit.Core.Credentials;
using EnvKit.Windows.Credentials;

namespace EnvKit.Windows.Tests.Credentials;

/// <summary>
/// Uses the real Credential Manager, under a unique target prefix that is removed afterwards,
/// so no existing credential is read back or touched.
/// </summary>
public sealed class WindowsCredentialStoreTests : IDisposable
{
    private readonly string _prefix = $"EnvKit.Tests/{Guid.NewGuid():N}/";
    private readonly WindowsCredentialStore _store = new();

    public void Dispose()
    {
        foreach (var credential in _store.List(_prefix + "*"))
        {
            _store.Delete(credential.Key);
        }
    }

    private CredentialKey Key(string name) => new(_prefix + name, CredentialKind.Generic);

    private static CredentialState State(string password, string user = "me") =>
        new(user, Encoding.Unicode.GetBytes(password), CredentialPersistence.LocalMachine);

    [Fact]
    public void Round_trips_every_field()
    {
        var state = new CredentialState(
            "me@example.com",
            Encoding.UTF8.GetBytes("ghp_token"),
            CredentialPersistence.Enterprise,
            "made by tests",
            "alias",
            [new CredentialAttributeEntry("tool:version", 0, [1, 2, 3])]);

        _store.Write(Key("full"), state);
        var read = _store.Get(Key("full"));

        Assert.NotNull(read);
        Assert.Equal(state, read.State);
        Assert.Equal("ghp_token", read.State.SecretText);
        Assert.True(read.LastWritten > DateTimeOffset.Now.AddMinutes(-5));
    }

    [Fact]
    public void Filter_lists_only_matching_targets()
    {
        _store.Write(Key("a"), State("1"));
        _store.Write(Key("b"), State("2"));

        var listed = _store.List(_prefix + "*");

        Assert.Equal([Key("a"), Key("b")], listed.Select(c => c.Key).OrderBy(k => k.TargetName, StringComparer.Ordinal));
    }

    [Fact]
    public void Unfiltered_listing_includes_ours_with_a_usable_key()
    {
        _store.Write(Key("all"), State("1"));

        var listed = Assert.Single(_store.List(), c => c.Key == Key("all"));
        Assert.NotNull(_store.Get(listed.Key));
    }

    [Fact]
    public void Overwrites_and_deletes()
    {
        _store.Write(Key("x"), State("old"));
        _store.Write(Key("x"), State("new", "you"));

        Assert.Equal("new", _store.Get(Key("x"))!.State.SecretText);
        Assert.Equal("you", _store.Get(Key("x"))!.State.UserName);
        Assert.True(_store.Delete(Key("x")));
        Assert.Null(_store.Get(Key("x")));
        Assert.False(_store.Delete(Key("x")));
    }

    [Fact]
    public void Empty_secret_is_allowed()
    {
        _store.Write(Key("empty"), State(string.Empty));

        Assert.Empty(_store.Get(Key("empty"))!.State.Secret!);
    }

    [Fact]
    public void Missing_filter_match_is_an_empty_list() =>
        Assert.Empty(_store.List(_prefix + "nothing-here*"));

    [Fact]
    public void Rejects_secrets_over_the_limit() =>
        Assert.Throws<ArgumentException>(() =>
            _store.Write(Key("big"), new CredentialState("me", new byte[CredentialState.MaxSecretBytes + 1], CredentialPersistence.LocalMachine)));

    [Fact]
    public void Refuses_to_write_domain_credentials() =>
        Assert.Throws<NotSupportedException>(() =>
            _store.Write(new CredentialKey(_prefix + "srv", CredentialKind.DomainPassword), State("x")));
}
