using System.CommandLine;
using EnvKit.Windows.Elevation;

namespace EnvKit.Cli.Commands;

/// <summary>The elevated helper entry point; started by <see cref="WindowsElevation"/>, not by people.</summary>
internal static class ApplyPlanCommand
{
    public static Command Create(CliServices services)
    {
        var file = new Argument<string>("file");
        var hash = new Option<string>(ElevatedPlanHost.HashOption) { Required = true };
        var command = new Command(ElevatedPlanHost.Verb, "Apply a machine-scope plan file (used by the UAC helper).")
        {
            file, hash,
        };
        command.Hidden = true;
        command.SetAction(result =>
            ElevatedPlanHost.Run(result.GetRequiredValue(file), result.GetRequiredValue(hash), services.Store));
        return command;
    }
}
