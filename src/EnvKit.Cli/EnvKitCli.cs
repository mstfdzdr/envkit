using System.CommandLine;
using EnvKit.Cli.Commands;

namespace EnvKit.Cli;

public static class EnvKitCli
{
    public static RootCommand Create(CliServices services) => new("Manage Windows environment variables and PATH safely.")
    {
        PathCommands.Create(services),
        WhichCommand.Create(services),
        VarCommands.Create(services),
        SnapshotCommands.Create(services),
    };

    public static int Run(CliServices services, string[] args)
    {
        ArgumentNullException.ThrowIfNull(services);
        var configuration = new InvocationConfiguration { Output = services.Out, Error = services.Error };
        return Create(services).Parse(args).Invoke(configuration);
    }
}
