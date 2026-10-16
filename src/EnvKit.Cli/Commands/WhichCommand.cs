using System.CommandLine;
using EnvKit.Core.Paths;
using EnvKit.Core.Variables;

namespace EnvKit.Cli.Commands;

internal static class WhichCommand
{
    public static Command Create(CliServices services)
    {
        var name = new Argument<string>("command") { Description = "Command name, e.g. python or git.exe." };
        var command = new Command("which", "Show which file runs for a command, and which ones it shadows.") { name };
        command.SetAction(result =>
        {
            var commandName = result.GetRequiredValue(name);
            var pathExt = services.Lookup().Resolve("PATHEXT", EnvScope.User);
            var matches = new CommandResolver(services.FileSystem).FindAll(commandName, services.AnalyzePath(), pathExt);
            if (matches.Count == 0)
            {
                services.Error.WriteLine($"{commandName}: not found in PATH.");
                return ExitCodes.Error;
            }

            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                var notes = new List<string> { $"{match.Entry.Scope}#{match.Entry.Index}" };
                if (match.IsAppExecutionAlias)
                {
                    notes.Add("Store alias");
                }

                if (i > 0)
                {
                    notes.Add("shadowed");
                }

                services.Out.WriteLine($"{(i == 0 ? "->" : "  ")} {match.FullPath}   ({string.Join(", ", notes)})");
            }

            return ExitCodes.Ok;
        });
        return command;
    }
}
