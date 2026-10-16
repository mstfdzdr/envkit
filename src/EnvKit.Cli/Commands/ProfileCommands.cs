using System.CommandLine;
using System.Text.Json;
using EnvKit.Core.Profiles;
using EnvKit.Core.Vault;

namespace EnvKit.Cli.Commands;

internal static class ProfileCommands
{
    public static Command Create(CliServices services) => new("profile", "Named sets of environment changes, e.g. switching JDKs.")
    {
        List(services),
        Show(services),
        New(services),
        Apply(services),
        Delete(services),
        Import(services),
        Export(services),
    };

    private static Command List(CliServices services)
    {
        var command = new Command("list", "List profiles.");
        command.SetAction(_ =>
        {
            var profiles = services.Profiles.List();
            foreach (var profile in profiles)
            {
                var vault = profile.UsesVault ? "  (uses vault: run only)" : string.Empty;
                var description = profile.Description is null ? string.Empty : $"  {profile.Description}";
                services.Out.WriteLine($"{profile.Name}  [{profile.Actions.Count} action(s)]{description}{vault}");
            }

            if (profiles.Count == 0)
            {
                services.Out.WriteLine("No profiles yet. Create one with: envkit profile new <name> --set NAME=VALUE --prepend-path DIR");
            }

            return ExitCodes.Ok;
        });
        return command;
    }

    private static Command Show(CliServices services)
    {
        var name = new Argument<string>("name");
        var command = new Command("show", "Show a profile's actions.") { name };
        command.SetAction(result =>
        {
            if (Load(services, result.GetRequiredValue(name)) is not { } profile)
            {
                return ExitCodes.Error;
            }

            services.Out.WriteLine(profile.Description is null ? profile.Name : $"{profile.Name}: {profile.Description}");
            foreach (var action in profile.Actions)
            {
                services.Out.WriteLine($"  {action.Describe()}");
            }

            return ExitCodes.Ok;
        });
        return command;
    }

    private static Command New(CliServices services)
    {
        var name = new Argument<string>("name");
        var description = new Option<string?>("--description", "-d");
        var set = new Option<string[]>("--set") { Description = "NAME=VALUE; VALUE may use %VARS% and ${vault:NAME}.", AllowMultipleArgumentsPerToken = false };
        var unset = new Option<string[]>("--unset") { Description = "Variable to remove." };
        var removePath = new Option<string[]>("--remove-path") { Description = "Directory to take out of PATH." };
        var prependPath = new Option<string[]>("--prepend-path") { Description = "Directory to put first in PATH." };
        var appendPath = new Option<string[]>("--append-path") { Description = "Directory to add at the end of PATH." };
        var force = new Option<bool>("--force") { Description = "Replace an existing profile." };
        var command = new Command(
            "new",
            "Create a profile. Actions run in this order: --set, --unset, --remove-path, --prepend-path, --append-path. For any other order, write the JSON and use profile import.")
        {
            name, description, set, unset, removePath, prependPath, appendPath, force,
        };
        command.SetAction(result =>
        {
            var actions = new List<ProfileAction>();
            foreach (var assignment in result.GetValue(set) ?? [])
            {
                var separator = assignment.IndexOf('=', StringComparison.Ordinal);
                if (separator <= 0)
                {
                    services.Error.WriteLine($"--set expects NAME=VALUE, got '{assignment}'.");
                    return ExitCodes.Error;
                }

                actions.Add(new SetVariableAction(assignment[..separator], assignment[(separator + 1)..]));
            }

            actions.AddRange((result.GetValue(unset) ?? []).Select(n => new UnsetVariableAction(n)));
            actions.AddRange((result.GetValue(removePath) ?? []).Select(d => new RemovePathAction(d)));
            actions.AddRange((result.GetValue(prependPath) ?? []).Select(d => new PrependPathAction(d)));
            actions.AddRange((result.GetValue(appendPath) ?? []).Select(d => new AppendPathAction(d)));
            if (actions.Count == 0)
            {
                services.Error.WriteLine("A profile needs at least one action (--set, --unset, --remove-path, --prepend-path or --append-path).");
                return ExitCodes.Error;
            }

            return Save(services, new Profile(result.GetRequiredValue(name), actions, result.GetValue(description)), result.GetValue(force));
        });
        return command;
    }

    private static Command Apply(CliServices services)
    {
        var name = new Argument<string>("name");
        var dryRun = Options.DryRun();
        var yes = Options.Yes();
        var command = new Command("apply", "Apply a profile to the user environment persistently (undoable).") { name, dryRun, yes };
        command.SetAction(result =>
        {
            if (Load(services, result.GetRequiredValue(name)) is not { } profile)
            {
                return ExitCodes.Error;
            }

            try
            {
                var plan = services.NewPlan().Apply(profile).Build($"Apply profile {profile.Name}");
                return new PlanRunner(services).Run(plan, result.GetValue(dryRun), result.GetValue(yes));
            }
            catch (InvalidOperationException ex)
            {
                services.Error.WriteLine(ex.Message);
                return ExitCodes.Error;
            }
        });
        return command;
    }

    private static Command Delete(CliServices services)
    {
        var name = new Argument<string>("name");
        var yes = Options.Yes();
        var command = new Command("delete", "Delete a profile (its effects on the environment stay).") { name, yes };
        command.SetAction(result =>
        {
            var profileName = result.GetRequiredValue(name);
            if (Load(services, profileName) is null)
            {
                return ExitCodes.Error;
            }

            if (!result.GetValue(yes) && !VaultCommands.Confirm(services, $"Delete the profile '{profileName}'?"))
            {
                return ExitCodes.Aborted;
            }

            services.Profiles.Delete(profileName);
            services.Out.WriteLine("Deleted.");
            return ExitCodes.Ok;
        });
        return command;
    }

    private static Command Import(CliServices services)
    {
        var file = new Argument<FileInfo>("file");
        var force = new Option<bool>("--force") { Description = "Replace an existing profile with the same name." };
        var command = new Command("import", "Add a profile from a JSON file.") { file, force };
        command.SetAction(result =>
        {
            Profile profile;
            try
            {
                profile = ProfileJson.Deserialize(File.ReadAllText(result.GetRequiredValue(file).FullName));
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or IOException)
            {
                services.Error.WriteLine($"Not a valid profile: {ex.Message}");
                return ExitCodes.Error;
            }

            return Save(services, profile, result.GetValue(force));
        });
        return command;
    }

    private static Command Export(CliServices services)
    {
        var name = new Argument<string>("name");
        var file = new Argument<FileInfo>("file");
        var command = new Command("export", "Write a profile as JSON (profiles hold vault references, never secrets).") { name, file };
        command.SetAction(result =>
        {
            if (Load(services, result.GetRequiredValue(name)) is not { } profile)
            {
                return ExitCodes.Error;
            }

            var path = result.GetRequiredValue(file).FullName;
            File.WriteAllText(path, ProfileJson.Serialize(profile));
            services.Out.WriteLine($"Wrote {path}.");
            return ExitCodes.Ok;
        });
        return command;
    }

    private static int Save(CliServices services, Profile profile, bool force)
    {
        try
        {
            profile.Validate();
        }
        catch (ArgumentException ex)
        {
            services.Error.WriteLine(ex.Message);
            return ExitCodes.Error;
        }

        if (!force && services.Profiles.Get(profile.Name) is not null)
        {
            services.Error.WriteLine($"A profile named '{profile.Name}' exists; pass --force to replace it.");
            return ExitCodes.Error;
        }

        services.Profiles.Save(profile);
        var missing = profile.Actions.OfType<SetVariableAction>()
            .SelectMany(a => VaultReference.Names(a.Value))
            .Where(n => services.Vault.Get(n) is null)
            .Distinct(VaultNames.Comparer)
            .ToList();
        services.Out.WriteLine($"Saved profile '{profile.Name}'.");
        if (missing.Count > 0)
        {
            services.Out.WriteLine($"Note: not in the vault yet: {string.Join(", ", missing)} (envkit vault set <name>).");
        }

        return ExitCodes.Ok;
    }

    internal static Profile? Load(CliServices services, string name)
    {
        try
        {
            if (services.Profiles.Get(name) is { } profile)
            {
                return profile;
            }

            services.Error.WriteLine($"No profile '{name}'.");
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            services.Error.WriteLine($"Profile '{name}' cannot be read: {ex.Message}");
        }

        return null;
    }
}
