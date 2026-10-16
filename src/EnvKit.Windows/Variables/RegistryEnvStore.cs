using System.Security;
using EnvKit.Core;
using EnvKit.Core.Variables;
using Microsoft.Win32;

namespace EnvKit.Windows.Variables;

/// <summary>
/// Reads and writes persistent variables directly in the registry. Unlike
/// <see cref="Environment.SetEnvironmentVariable(string, string?, EnvironmentVariableTarget)"/>,
/// this keeps REG_EXPAND_SZ values intact.
/// </summary>
public sealed class RegistryEnvStore(RegistryLocation user, RegistryLocation machine) : IEnvStore
{
    public RegistryEnvStore()
        : this(RegistryLocation.UserEnvironment, RegistryLocation.MachineEnvironment)
    {
    }

    public IReadOnlyList<EnvVariable> GetAll(EnvScope scope)
    {
        using var key = OpenRead(scope);
        if (key is null)
        {
            return [];
        }

        var variables = new List<EnvVariable>();
        foreach (var name in key.GetValueNames())
        {
            if (TryRead(key, name, scope) is { } variable)
            {
                variables.Add(variable);
            }
        }

        return variables;
    }

    public EnvVariable? Get(string name, EnvScope scope)
    {
        EnvVariable.ValidateName(name);
        using var key = OpenRead(scope);
        return key is null ? null : TryRead(key, name, scope);
    }

    public void Set(EnvVariable variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        variable.EnsureWritable();
        var kind = variable.Kind == EnvValueKind.Expandable ? RegistryValueKind.ExpandString : RegistryValueKind.String;
        Write(variable.Scope, key => key.SetValue(variable.Name, variable.Value, kind));
    }

    public bool Delete(string name, EnvScope scope)
    {
        // Checking first keeps a no-op delete from demanding write access (and elevation).
        if (Get(name, scope) is null)
        {
            return false;
        }

        Write(scope, key => key.DeleteValue(name, throwOnMissingValue: false));
        return true;
    }

    private RegistryLocation Location(EnvScope scope) => scope switch
    {
        EnvScope.User => user,
        EnvScope.Machine => machine,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };

    private RegistryKey? OpenRead(EnvScope scope)
    {
        var location = Location(scope);
        using var hive = RegistryKey.OpenBaseKey(location.Hive, RegistryView.Default);
        return hive.OpenSubKey(location.SubKey, writable: false);
    }

    private void Write(EnvScope scope, Action<RegistryKey> write)
    {
        var location = Location(scope);
        try
        {
            using var hive = RegistryKey.OpenBaseKey(location.Hive, RegistryView.Default);
            using var key = hive.CreateSubKey(location.SubKey, writable: true);
            write(key);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            throw new ElevationRequiredException($@"Writing to {location} requires administrator rights.", ex);
        }
    }

    private static EnvVariable? TryRead(RegistryKey key, string name, EnvScope scope)
    {
        // The default read expands REG_EXPAND_SZ; writing that result back would bake in the
        // current values and drop the %VAR% references.
        if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string value)
        {
            return null;
        }

        RegistryValueKind kind;
        try
        {
            kind = key.GetValueKind(name);
        }
        catch (IOException)
        {
            // Deleted between the two reads.
            return null;
        }

        try
        {
            return new EnvVariable(
                name, value, scope, kind == RegistryValueKind.ExpandString ? EnvValueKind.Expandable : EnvValueKind.Literal);
        }
        catch (ArgumentException)
        {
            // The unnamed default value, or a name Windows would never hand to a process.
            return null;
        }
    }
}
