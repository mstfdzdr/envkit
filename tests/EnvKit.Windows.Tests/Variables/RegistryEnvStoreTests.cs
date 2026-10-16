using System.Security.AccessControl;
using System.Security.Principal;
using EnvKit.Core;
using EnvKit.Core.Variables;
using EnvKit.Windows.Variables;
using Microsoft.Win32;

namespace EnvKit.Windows.Tests.Variables;

/// <summary>
/// Runs against a throwaway HKCU key standing in for both scopes, so the real
/// environment is never written.
/// </summary>
public sealed class RegistryEnvStoreTests : IDisposable
{
    private readonly string _root = $@"Software\EnvKit.Tests-{Guid.NewGuid():N}";
    private readonly RegistryEnvStore _store;

    public RegistryEnvStoreTests() =>
        _store = new RegistryEnvStore(
            new RegistryLocation(RegistryHive.CurrentUser, _root + @"\User"),
            new RegistryLocation(RegistryHive.CurrentUser, _root + @"\Machine"));

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);

    [Fact]
    public void Expandable_value_round_trips_without_being_expanded()
    {
        _store.Set(new EnvVariable("TOOLS", @"%USERPROFILE%\tools", EnvScope.User, EnvValueKind.Expandable));

        var read = _store.Get("TOOLS", EnvScope.User);

        Assert.NotNull(read);
        Assert.Equal(@"%USERPROFILE%\tools", read.Value);
        Assert.Equal(EnvValueKind.Expandable, read.Kind);
        Assert.Equal(RegistryValueKind.ExpandString, RawKind("User", "TOOLS"));
    }

    [Fact]
    public void Literal_value_is_written_as_REG_SZ()
    {
        _store.Set(new EnvVariable("PLAIN", "%NOT_A_REF%", EnvScope.User));

        Assert.Equal(RegistryValueKind.String, RawKind("User", "PLAIN"));
        Assert.Equal(EnvValueKind.Literal, _store.Get("PLAIN", EnvScope.User)!.Kind);
    }

    [Fact]
    public void Overwriting_can_change_kind()
    {
        _store.Set(new EnvVariable("X", "a", EnvScope.User));
        _store.Set(new EnvVariable("X", "%TEMP%", EnvScope.User, EnvValueKind.Expandable));

        Assert.Equal(RegistryValueKind.ExpandString, RawKind("User", "X"));
    }

    [Fact]
    public void Get_is_case_insensitive()
    {
        _store.Set(new EnvVariable("JAVA_HOME", @"C:\jdk", EnvScope.User));

        Assert.Equal(@"C:\jdk", _store.Get("java_home", EnvScope.User)?.Value);
    }

    [Fact]
    public void Missing_key_reads_as_empty()
    {
        Assert.Empty(_store.GetAll(EnvScope.Machine));
        Assert.Null(_store.Get("ANY", EnvScope.Machine));
    }

    [Fact]
    public void Scopes_are_isolated()
    {
        _store.Set(new EnvVariable("ONLY_USER", "1", EnvScope.User));

        Assert.Null(_store.Get("ONLY_USER", EnvScope.Machine));
        Assert.All(_store.GetAll(EnvScope.User), v => Assert.Equal(EnvScope.User, v.Scope));
    }

    [Fact]
    public void GetAll_skips_non_string_values()
    {
        _store.Set(new EnvVariable("TEXT", "1", EnvScope.User));
        using (var key = Registry.CurrentUser.OpenSubKey(_root + @"\User", writable: true)!)
        {
            key.SetValue("NUMBER", 1, RegistryValueKind.DWord);
            key.SetValue(string.Empty, "unnamed default value");
        }

        var names = _store.GetAll(EnvScope.User).Select(v => v.Name);

        Assert.Equal(["TEXT"], names);
    }

    [Fact]
    public void Delete_reports_whether_the_variable_existed()
    {
        _store.Set(new EnvVariable("GONE", "1", EnvScope.User));

        Assert.True(_store.Delete("gone", EnvScope.User));
        Assert.Null(_store.Get("GONE", EnvScope.User));
        Assert.False(_store.Delete("GONE", EnvScope.User));
    }

    [Fact]
    public void Access_denied_surfaces_as_elevation_required()
    {
        // An explicit deny ACE blocks administrators too, so this behaves the same
        // whether or not the test run is elevated.
        var lockedPath = _root + @"\Locked";
        using var identity = WindowsIdentity.GetCurrent();
        var deny = new RegistryAccessRule(
            identity.User!, RegistryRights.SetValue | RegistryRights.CreateSubKey, AccessControlType.Deny);
        using (var locked = Registry.CurrentUser.CreateSubKey(lockedPath))
        {
            var security = locked.GetAccessControl();
            security.AddAccessRule(deny);
            locked.SetAccessControl(security);
        }

        try
        {
            var store = new RegistryEnvStore(
                new RegistryLocation(RegistryHive.CurrentUser, lockedPath),
                new RegistryLocation(RegistryHive.CurrentUser, _root + @"\Machine"));

            Assert.Throws<ElevationRequiredException>(
                () => store.Set(new EnvVariable("X", "1", EnvScope.User)));
        }
        finally
        {
            // Lift the deny so Dispose can delete the tree.
            using var locked = Registry.CurrentUser.OpenSubKey(
                lockedPath,
                RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryRights.ReadPermissions | RegistryRights.ChangePermissions)!;
            var security = locked.GetAccessControl();
            security.RemoveAccessRule(deny);
            locked.SetAccessControl(security);
        }
    }

    [Fact]
    public void Reads_the_real_machine_environment()
    {
        var store = new RegistryEnvStore();

        Assert.Contains(store.GetAll(EnvScope.Machine), v => EnvVariable.NameComparer.Equals(v.Name, "Path"));
    }

    private RegistryValueKind RawKind(string scopeKey, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{_root}\{scopeKey}")!;
        return key.GetValueKind(name);
    }
}
