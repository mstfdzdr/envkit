using System.CommandLine;
using System.Globalization;
using EnvKit.Core.Credentials;
using EnvKit.Core.Security;

namespace EnvKit.Cli.Commands;

internal static class CredCommands
{
    public static Command Create(CliServices services) => new("cred", "Inspect and edit Windows Credential Manager entries.")
    {
        List(services),
        Show(services),
        Set(services),
        Delete(services),
        Export(services),
    };

    private static Option<string?> Filter() => new("--filter", "-f")
    {
        Description = "Target filter, e.g. git:* (a trailing * only).",
    };

    private static Option<CredentialGroup?> Group() => new("--group", "-g")
    {
        Description = "Only credentials of one tool: Git, GitHub, VisualStudio, VSCode, Docker, RemoteDesktop, MicrosoftAccount, Office, Network, Other.",
    };

    private static Option<CredentialKind> Kind() => new("--type", "-t")
    {
        Description = "Credential type (default Generic).",
        DefaultValueFactory = _ => CredentialKind.Generic,
    };

    private static Command List(CliServices services)
    {
        var filter = Filter();
        var group = Group();
        var command = new Command("list", "List credentials grouped by the tool they belong to. Secrets are never shown.") { filter, group };
        command.SetAction(result =>
        {
            var credentials = Select(services, result.GetValue(filter), result.GetValue(group));
            foreach (var byGroup in credentials.GroupBy(c => CredentialClassifier.Classify(c.Key)).OrderBy(g => g.Key))
            {
                services.Out.WriteLine($"{byGroup.Key}:");
                foreach (var credential in byGroup.OrderBy(c => c.TargetName, CredentialKey.TargetComparer))
                {
                    var kind = credential.Kind == CredentialKind.Generic ? string.Empty : $" [{credential.Kind}]";
                    var written = credential.LastWritten.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    services.Out.WriteLine($"  {credential.TargetName}{kind}   user: {credential.State.UserName ?? "-"}   {credential.State.Persistence}, {written}");
                }
            }

            services.Out.WriteLine($"{credentials.Count} credential(s).");
            return ExitCodes.Ok;
        });
        return command;
    }

    private static Command Show(CliServices services)
    {
        var target = new Argument<string>("target");
        var kind = Kind();
        var reveal = new Option<bool>("--reveal") { Description = "Print the secret." };
        var command = new Command("show", "Show one credential; the secret only with --reveal.") { target, kind, reveal };
        command.SetAction(result =>
        {
            var key = new CredentialKey(result.GetRequiredValue(target), result.GetRequiredValue(kind));
            if (services.Credentials.Get(key) is not { } credential)
            {
                services.Error.WriteLine($"No credential {key}.");
                return ExitCodes.Error;
            }

            var state = credential.State;
            var output = services.Out;
            output.WriteLine($"Target:      {credential.TargetName}");
            output.WriteLine($"Type:        {credential.Kind}");
            output.WriteLine($"Tool:        {CredentialClassifier.Classify(credential.Key)}");
            output.WriteLine($"User:        {state.UserName ?? "-"}");
            output.WriteLine($"Persistence: {state.Persistence}");
            output.WriteLine($"Written:     {credential.LastWritten.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            if (state.Comment is not null)
            {
                output.WriteLine($"Comment:     {state.Comment}");
            }

            output.WriteLine($"Secret:      {(state.Secret is null ? "(Windows does not reveal it)" : result.GetValue(reveal) ? state.SecretText : SecretDetector.AsciiMask)}");
            return ExitCodes.Ok;
        });
        return command;
    }

    private static Command Set(CliServices services)
    {
        var target = new Argument<string>("target");
        var user = new Option<string?>("--user", "-u") { Description = "User name (kept if omitted when editing)." };
        var comment = new Option<string?>("--comment") { Description = "Comment (kept if omitted when editing)." };
        var persistence = new Option<CredentialPersistence?>("--persistence")
        {
            Description = "LocalMachine (default for new), Enterprise (roams) or Session.",
        };
        var fromStdin = new Option<bool>("--password-stdin") { Description = "Read the password from standard input instead of prompting." };
        var dryRun = Options.DryRun();
        var yes = Options.Yes();
        var command = new Command("set", "Create or update a generic credential. The password is prompted for, never passed as an argument.")
        {
            target, user, comment, persistence, fromStdin, dryRun, yes,
        };
        command.SetAction(result =>
        {
            var key = new CredentialKey(result.GetRequiredValue(target), CredentialKind.Generic);
            var existing = services.Credentials.Get(key)?.State;

            string? password;
            if (result.GetValue(fromStdin))
            {
                password = services.In.ReadLine();
            }
            else if (services.Prompt.IsInteractive)
            {
                password = services.Prompt.ReadSecret(existing is null ? "Password" : "New password (empty keeps the current one)");
            }
            else
            {
                services.Error.WriteLine("Input is redirected; pass --password-stdin to read the password from it.");
                return ExitCodes.Aborted;
            }

            if (password is null)
            {
                services.Error.WriteLine("No password was given.");
                return ExitCodes.Aborted;
            }

            var secret = password.Length == 0 && existing?.Secret is { } current
                ? current
                : CredentialState.EncodeSecret(password, existing?.Secret);
            var state = (existing ?? new CredentialState(null, null, CredentialPersistence.LocalMachine)) with
            {
                UserName = result.GetValue(user) ?? existing?.UserName,
                Comment = result.GetValue(comment) ?? existing?.Comment,
                Persistence = result.GetValue(persistence) ?? existing?.Persistence ?? CredentialPersistence.LocalMachine,
                Secret = secret,
            };

            try
            {
                var plan = services.NewPlan().SetCredential(key, state).Build($"Set credential {key}");
                return new PlanRunner(services).Run(plan, result.GetValue(dryRun), result.GetValue(yes));
            }
            catch (ArgumentException ex)
            {
                services.Error.WriteLine(ex.Message);
                return ExitCodes.Error;
            }
        });
        return command;
    }

    private static Command Delete(CliServices services)
    {
        var targets = new Argument<string[]>("targets") { Arity = ArgumentArity.ZeroOrMore, Description = "Targets to delete." };
        var kind = Kind();
        var filter = Filter();
        var group = Group();
        var dryRun = Options.DryRun();
        var yes = Options.Yes();
        var command = new Command("delete", "Delete credentials by target, filter or tool. Undo with envkit snapshot restore.")
        {
            targets, kind, filter, group, dryRun, yes,
        };
        command.SetAction(result =>
        {
            var names = result.GetValue(targets) ?? [];
            var byFilter = result.GetValue(filter);
            var byGroup = result.GetValue(group);
            if (names.Length == 0 && byFilter is null && byGroup is null)
            {
                services.Error.WriteLine("Name targets, or use --filter or --group.");
                return ExitCodes.Error;
            }

            var keys = new List<CredentialKey>();
            foreach (var name in names)
            {
                var key = new CredentialKey(name, result.GetRequiredValue(kind));
                if (services.Credentials.Get(key) is null)
                {
                    services.Error.WriteLine($"No credential {key}.");
                    return ExitCodes.Error;
                }

                keys.Add(key);
            }

            if (byFilter is not null || byGroup is not null)
            {
                keys.AddRange(Select(services, byFilter, byGroup).Select(c => c.Key));
            }

            var builder = services.NewPlan();
            foreach (var key in keys.Distinct())
            {
                builder.DeleteCredential(key);
            }

            var plan = builder.Build($"Delete {keys.Distinct().Count()} credential(s)");
            return new PlanRunner(services).Run(plan, result.GetValue(dryRun), result.GetValue(yes));
        });
        return command;
    }

    private static Command Export(CliServices services)
    {
        var file = new Argument<FileInfo>("file") { Description = "JSON file to write." };
        var filter = Filter();
        var group = Group();
        var command = new Command("export", "Write an inventory of credentials as JSON, without any secrets.") { file, filter, group };
        command.SetAction(result =>
        {
            var credentials = Select(services, result.GetValue(filter), result.GetValue(group));
            var path = result.GetRequiredValue(file).FullName;
            File.WriteAllText(path, CredentialExport.ToJson(credentials));
            services.Out.WriteLine($"Wrote {credentials.Count} credential(s) to {path}. No secrets are included.");
            return ExitCodes.Ok;
        });
        return command;
    }

    private static List<Credential> Select(CliServices services, string? filter, CredentialGroup? group) =>
        [.. services.Credentials.List(filter).Where(c => group is null || CredentialClassifier.Classify(c.Key) == group)];
}
