using System.CommandLine;
using System.Text.Json;
using EnvKit.Core.DotEnv;
using EnvKit.Core.Plans;
using EnvKit.Core.Security;
using EnvKit.Core.Vault;

namespace EnvKit.Cli.Commands;

internal static class DotEnvCommands
{
    public static Command Create(CliServices services) => new("dotenv", "Find, inspect and edit .env files without disturbing their formatting.")
    {
        Scan(services),
        List(services),
        Get(services),
        Set(services),
        Unset(services),
        Rename(services),
        Link(services),
    };

    private static Option<string> File() => new("--file", "-f")
    {
        Description = "The .env file (default: .env in the current folder).",
        DefaultValueFactory = _ => ".env",
    };

    private static Command Scan(CliServices services)
    {
        var directory = new Argument<string>("folder") { Arity = ArgumentArity.ZeroOrOne, DefaultValueFactory = _ => "." };
        var command = new Command("scan", "Find .env files and user-secrets projects, and check them against git.") { directory };
        command.SetAction(result =>
        {
            var root = Path.GetFullPath(result.GetRequiredValue(directory));
            var found = ProjectScanner.Scan(root, services.Git);
            foreach (var file in found)
            {
                var shown = file.Format == FileFormat.UserSecrets
                    ? $"user secrets for {Path.GetRelativePath(root, file.Project!)} ({(services.Files.Read(file.Path) is null ? "no secrets yet" : "secrets.json")})"
                    : Path.GetRelativePath(root, file.Path) + (file.IsTemplate ? "   (template)" : string.Empty);
                services.Out.WriteLine(shown);
                foreach (var warning in file.WarningList)
                {
                    services.Out.WriteLine(warning == SecretFileWarning.Committed
                        ? "    ! committed to git: its values may already be in the history"
                        : "    ! not in .gitignore: 'git add .' would commit it");
                }
            }

            services.Out.WriteLine($"{found.Count} found.");
            return found.Any(f => f.WarningList.Count > 0) ? ExitCodes.Error : ExitCodes.Ok;
        });
        return command;
    }

    private static Command List(CliServices services)
    {
        var file = File();
        var showSecrets = new Option<bool>("--show-secrets") { Description = "Print values that look like secrets." };
        var command = new Command("list", "List keys and values (secret-looking values masked) and problems in the file.") { file, showSecrets };
        command.SetAction(result =>
        {
            if (Load(services, result.GetRequiredValue(file)) is not { } document)
            {
                return ExitCodes.Error;
            }

            var reveal = result.GetValue(showSecrets);
            foreach (var (key, value) in document.Values())
            {
                var shown = reveal || VaultReference.HasReferences(value) || !SecretDetector.LooksSecret(key, value) ? value : SecretDetector.AsciiMask;
                services.Out.WriteLine($"{key} = {shown}");
            }

            foreach (var key in document.DuplicateKeys)
            {
                services.Out.WriteLine($"! {key} is assigned more than once; the last one wins.");
            }

            foreach (var line in document.InvalidLines)
            {
                services.Out.WriteLine($"! line {line.LineNumber} is not KEY=VALUE and is ignored by loaders.");
            }

            return ExitCodes.Ok;
        });
        return command;
    }

    private static Command Get(CliServices services)
    {
        var key = new Argument<string>("key");
        var file = File();
        var command = new Command("get", "Print one value (for scripts).") { key, file };
        command.SetAction(result =>
        {
            if (Load(services, result.GetRequiredValue(file)) is not { } document)
            {
                return ExitCodes.Error;
            }

            if (document.Get(result.GetRequiredValue(key)) is not { } value)
            {
                services.Error.WriteLine($"{result.GetRequiredValue(key)} is not set.");
                return ExitCodes.Error;
            }

            services.Out.WriteLine(value);
            return ExitCodes.Ok;
        });
        return command;
    }

    private static Command Set(CliServices services)
    {
        var key = new Argument<string>("key");
        var value = new Argument<string?>("value") { Arity = ArgumentArity.ZeroOrOne, Description = "Omit to be prompted (keeps secrets out of shell history)." };
        var file = File();
        var fromStdin = new Option<bool>("--value-stdin") { Description = "Read the value from standard input." };
        var dryRun = Options.DryRun();
        var yes = Options.Yes();
        var command = new Command("set", "Set a key, changing only its value in place (created at the end if new).") { key, value, file, fromStdin, dryRun, yes };
        command.SetAction(result =>
        {
            var newValue = result.GetValue(value);
            if (newValue is null)
            {
                newValue = result.GetValue(fromStdin) ? StandardInput.ReadLine(services.In)
                    : services.Prompt.IsInteractive ? services.Prompt.ReadSecret($"Value for {result.GetRequiredValue(key)}")
                    : null;
                if (newValue is null)
                {
                    services.Error.WriteLine("No value: pass it as an argument, with --value-stdin, or from a terminal.");
                    return ExitCodes.Aborted;
                }
            }

            return Edit(services, result.GetRequiredValue(file), $"Set {result.GetRequiredValue(key)}", d => d.Set(result.GetRequiredValue(key), newValue), result.GetValue(dryRun), result.GetValue(yes));
        });
        return command;
    }

    private static Command Unset(CliServices services)
    {
        var key = new Argument<string>("key");
        var file = File();
        var dryRun = Options.DryRun();
        var yes = Options.Yes();
        var command = new Command("unset", "Remove every assignment of a key.") { key, file, dryRun, yes };
        command.SetAction(result =>
        {
            var name = result.GetRequiredValue(key);
            if (Load(services, result.GetRequiredValue(file)) is not { } document)
            {
                return ExitCodes.Error;
            }

            if (document.Get(name) is null)
            {
                services.Error.WriteLine($"{name} is not set.");
                return ExitCodes.Error;
            }

            return Edit(services, result.GetRequiredValue(file), $"Remove {name}", d => d.Remove(name), result.GetValue(dryRun), result.GetValue(yes));
        });
        return command;
    }

    private static Command Rename(CliServices services)
    {
        var oldKey = new Argument<string>("key");
        var newKey = new Argument<string>("new-key");
        var file = File();
        var dryRun = Options.DryRun();
        var yes = Options.Yes();
        var command = new Command("rename", "Rename a key, keeping its value, quoting and comment.") { oldKey, newKey, file, dryRun, yes };
        command.SetAction(result =>
        {
            var from = result.GetRequiredValue(oldKey);
            var to = result.GetRequiredValue(newKey);
            return Edit(
                services,
                result.GetRequiredValue(file),
                $"Rename {from} to {to}",
                d =>
                {
                    if (d.Rename(from, to) == 0)
                    {
                        throw new ArgumentException($"{from} is not set.");
                    }
                },
                result.GetValue(dryRun),
                result.GetValue(yes));
        });
        return command;
    }

    private static Command Link(CliServices services)
    {
        var key = new Argument<string>("key");
        var secret = new Argument<string>("vault-name");
        var file = File();
        var move = new Option<bool>("--move") { Description = "First store the key's current value in the vault under that name." };
        var dryRun = Options.DryRun();
        var yes = Options.Yes();
        var command = new Command(
            "link",
            "Replace a value with ${vault:NAME}, so the secret lives in the vault and envkit run --env-file fills it in.")
        {
            key, secret, file, move, dryRun, yes,
        };
        command.SetAction(result =>
        {
            var name = result.GetRequiredValue(key);
            var vaultName = result.GetRequiredValue(secret);
            if (!VaultNames.IsValid(vaultName))
            {
                services.Error.WriteLine($"'{vaultName}' is not a valid vault name.");
                return ExitCodes.Error;
            }

            if (Load(services, result.GetRequiredValue(file)) is not { } document)
            {
                return ExitCodes.Error;
            }

            if (result.GetValue(move))
            {
                if (document.Get(name) is not { } current || VaultReference.HasReferences(current))
                {
                    services.Error.WriteLine($"{name} has no plain value to move.");
                    return ExitCodes.Error;
                }

                if (services.Vault.Get(vaultName) is { } existing && existing != current)
                {
                    services.Error.WriteLine($"The vault already has a different '{vaultName}'; pick another name or remove it first.");
                    return ExitCodes.Error;
                }

                var plan = services.NewPlan().EditDotEnv(result.GetRequiredValue(file), d => d.Set(name, VaultReference.For(vaultName))).Build($"Move {name} into the vault as {vaultName}");
                var stored = false;

                // The vault write waits for the confirmation, and comes before the file loses the value.
                var exit = new PlanRunner(services).Run(plan, result.GetValue(dryRun), result.GetValue(yes), () =>
                {
                    services.Vault.Set(vaultName, current);
                    stored = true;
                });
                if (exit != ExitCodes.Ok && stored)
                {
                    services.Out.WriteLine($"The value is also in the vault as '{vaultName}'; delete it with envkit vault delete {vaultName} if unwanted.");
                }

                return exit;
            }

            if (services.Vault.Get(vaultName) is null)
            {
                services.Out.WriteLine($"Note: '{vaultName}' is not in the vault yet (envkit vault set {vaultName}).");
            }

            return Edit(services, result.GetRequiredValue(file), $"Link {name} to the vault", d => d.Set(name, VaultReference.For(vaultName)), result.GetValue(dryRun), result.GetValue(yes));
        });
        return command;
    }

    private static int Edit(CliServices services, string file, string description, Action<DotEnvDocument> edit, bool dryRun, bool yes)
    {
        ChangePlan plan;
        try
        {
            plan = services.NewPlan().EditDotEnv(file, edit).Build($"{description} in {Path.GetFileName(file)}");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            services.Error.WriteLine(ex.Message);
            return ExitCodes.Error;
        }

        return new PlanRunner(services).Run(plan, dryRun, yes);
    }

    internal static DotEnvDocument? Load(CliServices services, string file)
    {
        var bytes = services.Files.Read(Path.GetFullPath(file));
        if (bytes is null)
        {
            services.Error.WriteLine($"{file} does not exist.");
            return null;
        }

        try
        {
            return DotEnvFile.Parse(bytes).Document;
        }
        catch (InvalidDataException ex)
        {
            services.Error.WriteLine($"{file}: {ex.Message}");
            return null;
        }
    }
}

internal static class UserSecretsCommands
{
    public static Command Create(CliServices services) => new("user-secrets", "Inspect and edit .NET user secrets (secrets.json) of a project.")
    {
        List(services),
        Set(services),
        Unset(services),
    };

    private static Option<string> Project() => new("--project", "-p")
    {
        Description = "The .csproj, or a folder containing one (default: the current folder).",
        DefaultValueFactory = _ => ".",
    };

    private static Command List(CliServices services)
    {
        var project = Project();
        var showSecrets = new Option<bool>("--show-secrets") { Description = "Print values that look like secrets." };
        var command = new Command("list", "List configuration keys (secret-looking values masked).") { project, showSecrets };
        command.SetAction(result =>
        {
            if (Resolve(services, result.GetRequiredValue(project)) is not { } path || Load(services, path) is not { } document)
            {
                return ExitCodes.Error;
            }

            var reveal = result.GetValue(showSecrets);
            foreach (var (key, value) in document.Values())
            {
                services.Out.WriteLine($"{key} = {(reveal || !SecretDetector.LooksSecret(key, value) ? value : SecretDetector.AsciiMask)}");
            }

            return ExitCodes.Ok;
        });
        return command;
    }

    private static Command Set(CliServices services)
    {
        var key = new Argument<string>("key") { Description = "Configuration key, e.g. Stripe:ApiKey." };
        var value = new Argument<string?>("value") { Arity = ArgumentArity.ZeroOrOne, Description = "Omit to be prompted." };
        var project = Project();
        var fromStdin = new Option<bool>("--value-stdin");
        var dryRun = Options.DryRun();
        var yes = Options.Yes();
        var command = new Command("set", "Set a user secret.") { key, value, project, fromStdin, dryRun, yes };
        command.SetAction(result =>
        {
            if (Resolve(services, result.GetRequiredValue(project)) is not { } path)
            {
                return ExitCodes.Error;
            }

            var newValue = result.GetValue(value)
                ?? (result.GetValue(fromStdin) ? StandardInput.ReadLine(services.In)
                    : services.Prompt.IsInteractive ? services.Prompt.ReadSecret($"Value for {result.GetRequiredValue(key)}") : null);
            if (newValue is null)
            {
                services.Error.WriteLine("No value: pass it as an argument, with --value-stdin, or from a terminal.");
                return ExitCodes.Aborted;
            }

            return Edit(services, path, $"Set {result.GetRequiredValue(key)}", d => d.Set(result.GetRequiredValue(key), newValue), result.GetValue(dryRun), result.GetValue(yes));
        });
        return command;
    }

    private static Command Unset(CliServices services)
    {
        var key = new Argument<string>("key");
        var project = Project();
        var dryRun = Options.DryRun();
        var yes = Options.Yes();
        var command = new Command("unset", "Remove a user secret.") { key, project, dryRun, yes };
        command.SetAction(result =>
        {
            if (Resolve(services, result.GetRequiredValue(project)) is not { } path)
            {
                return ExitCodes.Error;
            }

            var name = result.GetRequiredValue(key);
            return Edit(
                services,
                path,
                $"Remove {name}",
                d =>
                {
                    if (!d.Remove(name))
                    {
                        throw new ArgumentException($"{name} is not set.");
                    }
                },
                result.GetValue(dryRun),
                result.GetValue(yes));
        });
        return command;
    }

    private static int Edit(CliServices services, string path, string description, Action<UserSecretsDocument> edit, bool dryRun, bool yes)
    {
        ChangePlan plan;
        try
        {
            plan = services.NewPlan().EditUserSecrets(path, edit).Build($"{description} in user secrets");
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        {
            services.Error.WriteLine(ex.Message);
            return ExitCodes.Error;
        }

        return new PlanRunner(services).Run(plan, dryRun, yes);
    }

    private static UserSecretsDocument? Load(CliServices services, string path)
    {
        try
        {
            return UserSecretsDocument.Parse(services.Files.Read(path));
        }
        catch (JsonException ex)
        {
            services.Error.WriteLine($"secrets.json is not valid JSON: {ex.Message}");
            return null;
        }
    }

    /// <summary>The secrets.json of a .csproj, or of the single user-secrets project in a folder.</summary>
    private static string? Resolve(CliServices services, string project)
    {
        var full = Path.GetFullPath(project);
        var projects = full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            ? [full]
            : Directory.Exists(full) ? Directory.GetFiles(full, "*.csproj") : [];
        var withSecrets = projects.Select(p => (Project: p, Id: ProjectScanner.ReadUserSecretsId(p))).Where(p => p.Id is not null).ToList();
        if (withSecrets.Count != 1)
        {
            services.Error.WriteLine(withSecrets.Count == 0
                ? $"No project with a <UserSecretsId> at {project}. Run 'dotnet user-secrets init' first."
                : $"Several projects in {project}; pass one with --project.");
            return null;
        }

        if (ProjectScanner.UserSecretsPath(withSecrets[0].Id!) is not { } path)
        {
            services.Error.WriteLine($"The UserSecretsId in {Path.GetFileName(withSecrets[0].Project)} is not a safe folder name.");
            return null;
        }

        return path;
    }
}
