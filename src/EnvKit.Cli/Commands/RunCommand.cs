using System.CommandLine;
using EnvKit.Core.Profiles;
using EnvKit.Core.Vault;

namespace EnvKit.Cli.Commands;

internal static class RunCommand
{
    public static Command Create(CliServices services)
    {
        var profiles = new Option<string[]>("--profile", "-p") { Description = "Profile to apply first; repeatable, applied in order." };
        var secrets = new Option<string[]>("--secret", "-s")
        {
            Description = "NAME or NAME=SECRET: set variable NAME to a vault secret (named NAME unless given).",
        };
        var envFiles = new Option<string[]>("--env-file", "-e")
        {
            Description = ".env file to load after the profiles; ${vault:NAME} values are filled in. Repeatable.",
        };
        var commandLine = new Argument<string[]>("command")
        {
            Arity = ArgumentArity.OneOrMore,
            Description = "The command and its arguments, after --.",
        };
        var command = new Command(
            "run",
            "Run a command with profiles and vault secrets applied to its environment only. Nothing is written to the persistent environment. Example: envkit run -p java21 -s GITHUB_TOKEN -- mvn package")
        {
            profiles, envFiles, secrets, commandLine,
        };
        command.SetAction(result =>
        {
            var selected = new List<Profile>();
            foreach (var name in result.GetValue(profiles) ?? [])
            {
                if (ProfileCommands.Load(services, name) is not { } profile)
                {
                    return ExitCodes.Error;
                }

                selected.Add(profile);
            }

            Dictionary<string, string> environment;
            try
            {
                environment = ProfileEnvironment.Apply(services.ProcessEnvironment(), selected, services.Vault);
                foreach (var file in result.GetValue(envFiles) ?? [])
                {
                    if (DotEnvCommands.Load(services, file) is not { } document)
                    {
                        return ExitCodes.Error;
                    }

                    foreach (var (key, value) in document.Values())
                    {
                        environment[key] = VaultReference.Resolve(value, services.Vault);
                    }
                }

                foreach (var spec in result.GetValue(secrets) ?? [])
                {
                    var separator = spec.IndexOf('=', StringComparison.Ordinal);
                    var variable = separator < 0 ? spec : spec[..separator];
                    var secretName = separator < 0 ? spec : spec[(separator + 1)..];
                    environment[variable] = services.Vault.Get(secretName)
                        ?? throw new KeyNotFoundException($"Not in the vault: {secretName}.");
                }
            }
            catch (KeyNotFoundException ex)
            {
                services.Error.WriteLine($"{ex.Message} Add it with: envkit vault set <name>");
                return ExitCodes.Error;
            }

            var arguments = result.GetRequiredValue(commandLine);
            var executable = ProfileEnvironment.FindExecutable(arguments[0], environment, services.FileSystem.FileExists);
            if (executable is null)
            {
                services.Error.WriteLine($"{arguments[0]}: not found on the PATH of the new environment.");
                return ExitCodes.Error;
            }

            // The child's own exit code, so envkit run is transparent in scripts.
            return services.Processes.Run(executable, arguments[1..], environment);
        });
        return command;
    }
}
