using Microsoft.Win32;

namespace EnvKit.Windows.Variables;

public sealed record RegistryLocation(RegistryHive Hive, string SubKey)
{
    public static RegistryLocation UserEnvironment { get; } =
        new(RegistryHive.CurrentUser, "Environment");

    public static RegistryLocation MachineEnvironment { get; } =
        new(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment");

    public override string ToString() => $@"{Hive}\{SubKey}";
}
