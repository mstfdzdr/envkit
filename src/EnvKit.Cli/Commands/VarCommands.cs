using System.CommandLine;
using EnvKit.Core.Variables;

namespace EnvKit.Cli.Commands;

internal static class VarCommands
{
    private const int ListValueWidth = 100;

    public static Command Create(CliServices services) => new("var", "Inspect and edit environment variables.")
    {
        List(services),
        Get(services),
        Set(services),
        Unset(services),
    };

    private static Command List(CliServices services)
    {
        var scope = Options.ScopeFilter();
        var command = new Command("list", "List persistent variables.") { scope };
        command.SetAction(result =>
        {
            foreach (var current in Options.Scopes(result.GetValue(scope)))
            {
                services.Out.WriteLine($"{current}:");
                foreach (var variable in services.Store.GetAll(current).OrderBy(v => v.Name, EnvVariable.NameComparer))
                {
                    var value = variable.Value.Length > ListValueWidth ? variable.Value[..ListValueWidth] + "..." : variable.Value;
                    var marker = variable.Kind == EnvValueKind.Expandable ? " (expand)" : string.Empty;
                    services.Out.WriteLine($"  {variable.Name}{marker} = {value}");
                }
            }

            return ExitCodes.Ok;
        });
        return command;
    }

    private static Command Get(CliServices services)
    {
        var name = new Argument<string>("name");
        var scope = Options.ScopeFilter();
        var expand = new Option<bool>("--expand", "-e") { Description = "Resolve %VARIABLE% references." };
        var command = new Command("get", "Print a variable. With --scope, prints only the value (for scripts).")
        {
            name, scope, expand,
        };
        command.SetAction(result =>
        {
            var variableName = result.GetRequiredValue(name);
            var only = result.GetValue(scope);
            var found = false;
            foreach (var current in Options.Scopes(only))
            {
                if (services.Store.Get(variableName, current) is not { } variable)
                {
                    continue;
                }

                found = true;
                var value = result.GetValue(expand) ? services.Lookup().Expand(variable.Value, current).Value : variable.Value;
                services.Out.WriteLine(only is null ? $"{current}: {value}" : value);
            }

            if (!found)
            {
                services.Error.WriteLine($"{variableName} is not set.");
                return ExitCodes.Error;
            }

            return ExitCodes.Ok;
        });
        return command;
    }

    private static Command Set(CliServices services)
    {
        var name = new Argument<string>("name");
        var value = new Argument<string>("value");
        var scope = Options.Scope();
        var expandable = new Option<bool>("--expand") { Description = "Store as REG_EXPAND_SZ." };
        var literal = new Option<bool>("--literal") { Description = "Store as REG_SZ, even if it contains %...%." };
        var dryRun = Options.DryRun();
        var yes = Options.Yes();
        var command = new Command("set", "Create or change a variable. The kind is inferred unless given.")
        {
            name, value, scope, expandable, literal, dryRun, yes,
        };
        command.Validators.Add(result =>
        {
            if (result.GetValue(expandable) && result.GetValue(literal))
            {
                result.AddError("--expand and --literal cannot be combined.");
            }
        });
        command.SetAction(result =>
        {
            EnvValueKind? kind = result.GetValue(expandable) ? EnvValueKind.Expandable
                : result.GetValue(literal) ? EnvValueKind.Literal
                : null;
            var variableName = result.GetRequiredValue(name);
            var target = result.GetRequiredValue(scope);
            var plan = services.NewPlan()
                .Set(variableName, target, result.GetRequiredValue(value), kind)
                .Build($@"Set {target}\{variableName}");
            return new PlanRunner(services).Run(plan, result.GetValue(dryRun), result.GetValue(yes));
        });
        return command;
    }

    private static Command Unset(CliServices services)
    {
        var name = new Argument<string>("name");
        var scope = Options.Scope();
        var dryRun = Options.DryRun();
        var yes = Options.Yes();
        var command = new Command("unset", "Delete a variable.") { name, scope, dryRun, yes };
        command.SetAction(result =>
        {
            var variableName = result.GetRequiredValue(name);
            var target = result.GetRequiredValue(scope);
            if (services.Store.Get(variableName, target) is null)
            {
                services.Error.WriteLine($@"{target}\{variableName} is not set.");
                return ExitCodes.Error;
            }

            var plan = services.NewPlan().Delete(variableName, target).Build($@"Delete {target}\{variableName}");
            return new PlanRunner(services).Run(plan, result.GetValue(dryRun), result.GetValue(yes));
        });
        return command;
    }
}
