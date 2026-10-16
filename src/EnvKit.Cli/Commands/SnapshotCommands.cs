using System.CommandLine;
using System.Globalization;
using EnvKit.Core.Snapshots;

namespace EnvKit.Cli.Commands;

internal static class SnapshotCommands
{
    public static Command Create(CliServices services) => new("snapshot", "List, compare and restore automatic backups.")
    {
        List(services),
        Diff(services),
        Restore(services),
    };

    private static Command List(CliServices services)
    {
        var command = new Command("list", "List snapshots, newest first.");
        command.SetAction(_ =>
        {
            var snapshots = services.Snapshots.List();
            if (snapshots.Count == 0)
            {
                services.Out.WriteLine("No snapshots yet. One is taken before every change.");
            }

            foreach (var info in snapshots)
            {
                var local = info.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                services.Out.WriteLine($"{info.Id}  {local}  {info.DisplayReason}");
            }

            return ExitCodes.Ok;
        });
        return command;
    }

    private static Command Diff(CliServices services)
    {
        var id = new Argument<string>("id");
        var scope = Options.ScopeFilter();
        var command = new Command("diff", "Show what restoring a snapshot would change.") { id, scope };
        command.SetAction(result =>
        {
            if (Load(services, result.GetRequiredValue(id)) is not { } snapshot)
            {
                return ExitCodes.Error;
            }

            var plan = services.NewPlan().Restore(snapshot, result.GetValue(scope)).Build($"Restore {snapshot.Id}");
            if (plan.IsEmpty)
            {
                services.Out.WriteLine("The current state matches the snapshot.");
            }
            else
            {
                PlanPrinter.Print(plan, services.Out);
            }

            return ExitCodes.Ok;
        });
        return command;
    }

    private static Command Restore(CliServices services)
    {
        var id = new Argument<string>("id");
        var scope = Options.ScopeFilter();
        var dryRun = Options.DryRun();
        var yes = Options.Yes();
        var command = new Command("restore", "Return variables to a snapshot's state (itself undoable).")
        {
            id, scope, dryRun, yes,
        };
        command.SetAction(result =>
        {
            if (Load(services, result.GetRequiredValue(id)) is not { } snapshot)
            {
                return ExitCodes.Error;
            }

            var plan = services.NewPlan().Restore(snapshot, result.GetValue(scope)).Build($"Restore {snapshot.Id}");
            return new PlanRunner(services).Run(plan, result.GetValue(dryRun), result.GetValue(yes));
        });
        return command;
    }

    private static Snapshot? Load(CliServices services, string id)
    {
        try
        {
            return services.Snapshots.Load(id);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or ArgumentException)
        {
            services.Error.WriteLine(ex.Message);
            return null;
        }
    }
}
