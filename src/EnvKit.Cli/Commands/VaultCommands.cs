using System.CommandLine;
using EnvKit.Core.Vault;

namespace EnvKit.Cli.Commands;

internal static class VaultCommands
{
    public static Command Create(CliServices services) => new("vault", "Store secrets that envkit run injects into processes, never into the persistent environment.")
    {
        List(services),
        Set(services),
        Get(services),
        Delete(services),
    };

    private static Command List(CliServices services)
    {
        var command = new Command("list", "List secret names (never values).");
        command.SetAction(_ =>
        {
            var names = services.Vault.Names();
            foreach (var name in names)
            {
                services.Out.WriteLine(name);
            }

            if (names.Count == 0)
            {
                services.Out.WriteLine("The vault is empty. Add a secret with: envkit vault set <name>");
            }

            return ExitCodes.Ok;
        });
        return command;
    }

    private static Command Set(CliServices services)
    {
        var name = new Argument<string>("name") { Description = "Letters, digits, '.', '_' or '-'; referenced as ${vault:name}." };
        var fromStdin = new Option<bool>("--value-stdin") { Description = "Read the value from standard input instead of prompting." };
        var yes = Options.Yes();
        var command = new Command("set", "Add or replace a secret. The value is prompted for, never passed as an argument.") { name, fromStdin, yes };
        command.SetAction(result =>
        {
            var secretName = result.GetRequiredValue(name);
            if (!VaultNames.IsValid(secretName))
            {
                services.Error.WriteLine($"'{secretName}' is not a valid name: use letters, digits, '.', '_' or '-'.");
                return ExitCodes.Error;
            }

            if (services.Vault.Get(secretName) is not null && !result.GetValue(yes) && !Confirm(services, $"Replace the secret '{secretName}'?"))
            {
                return ExitCodes.Aborted;
            }

            string? value;
            if (result.GetValue(fromStdin))
            {
                value = StandardInput.ReadLine(services.In);
            }
            else if (services.Prompt.IsInteractive)
            {
                value = services.Prompt.ReadSecret($"Value for {secretName}");
            }
            else
            {
                services.Error.WriteLine("Input is redirected; pass --value-stdin to read the value from it.");
                return ExitCodes.Aborted;
            }

            if (string.IsNullOrEmpty(value))
            {
                services.Error.WriteLine("No value was given; nothing was stored.");
                return ExitCodes.Aborted;
            }

            services.Vault.Set(secretName, value);
            services.Out.WriteLine($"Stored. Reference it as {VaultReference.For(secretName)} in a profile, or: envkit run --secret {secretName} -- <command>");
            return ExitCodes.Ok;
        });
        return command;
    }

    private static Command Get(CliServices services)
    {
        var name = new Argument<string>("name");
        var command = new Command("get", "Print a secret's value (for scripts; prefer envkit run).") { name };
        command.SetAction(result =>
        {
            if (services.Vault.Get(result.GetRequiredValue(name)) is not { } value)
            {
                services.Error.WriteLine($"No secret '{result.GetRequiredValue(name)}'.");
                return ExitCodes.Error;
            }

            services.Out.WriteLine(value);
            return ExitCodes.Ok;
        });
        return command;
    }

    private static Command Delete(CliServices services)
    {
        var name = new Argument<string>("name");
        var yes = Options.Yes();
        var command = new Command("delete", "Delete a secret. This cannot be undone.") { name, yes };
        command.SetAction(result =>
        {
            var secretName = result.GetRequiredValue(name);
            if (services.Vault.Get(secretName) is null)
            {
                services.Error.WriteLine($"No secret '{secretName}'.");
                return ExitCodes.Error;
            }

            if (!result.GetValue(yes) && !Confirm(services, $"Delete the secret '{secretName}'? This cannot be undone."))
            {
                return ExitCodes.Aborted;
            }

            services.Vault.Delete(secretName);
            services.Out.WriteLine("Deleted.");
            return ExitCodes.Ok;
        });
        return command;
    }

    internal static bool Confirm(CliServices services, string question)
    {
        if (!services.Prompt.IsInteractive)
        {
            services.Error.WriteLine("Input is redirected, so there is no one to confirm. Pass --yes.");
            return false;
        }

        if (services.Prompt.Confirm(question))
        {
            return true;
        }

        services.Out.WriteLine("Cancelled.");
        return false;
    }
}
