using EnvKit.Core;
using EnvKit.Core.Plans;
using EnvKit.Core.Variables;

namespace EnvKit.Cli;

internal static class ExitCodes
{
    public const int Ok = 0;
    public const int Error = 1;
    public const int ElevationRequired = 3;
    public const int Conflict = 4;
    public const int Aborted = 5;
}

/// <summary>Preview, confirm, apply: the one path every writing command takes.</summary>
internal sealed class PlanRunner(CliServices services)
{
    public int Run(ChangePlan plan, bool dryRun, bool yes)
    {
        var output = services.Out;
        if (plan.IsEmpty)
        {
            output.WriteLine("Nothing to change.");
            return ExitCodes.Ok;
        }

        PlanPrinter.Print(plan, output);
        if (dryRun)
        {
            output.WriteLine("Dry run: nothing was written.");
            return ExitCodes.Ok;
        }

        if (!yes)
        {
            if (!services.Prompt.IsInteractive)
            {
                services.Error.WriteLine("Input is redirected, so there is no one to confirm. Pass --yes to apply.");
                return ExitCodes.Aborted;
            }

            if (!services.Prompt.Confirm("Apply these changes?"))
            {
                output.WriteLine("Cancelled.");
                return ExitCodes.Aborted;
            }
        }

        try
        {
            // Machine-scope changes trigger a UAC prompt here when this terminal is not elevated.
            var result = services.Executor().Execute(plan);
            output.WriteLine($"Applied. Undo with: envkit snapshot restore {result.Snapshot!.Id}");
            output.WriteLine(result.Broadcast
                ? "New terminals see the change; terminals that are already open keep their old environment."
                : "Running programs could not be notified; sign out and back in to pick up the change.");
            return ExitCodes.Ok;
        }
        catch (OperationCanceledException ex)
        {
            services.Error.WriteLine(ex.Message);
            return ExitCodes.Aborted;
        }
        catch (ElevationRequiredException)
        {
            services.Error.WriteLine("Machine-scope changes need administrator rights. Nothing was changed.");
            return ExitCodes.ElevationRequired;
        }
        catch (ElevatedApplyException ex)
        {
            services.Error.WriteLine($"{ex.Message} User-scope changes from this plan were reverted.");
            return ExitCodes.Error;
        }
        catch (PlanConflictException ex)
        {
            services.Error.WriteLine($"{ex.Message} Nothing was written; run the command again.");
            return ExitCodes.Conflict;
        }
        catch (PlanApplyException ex)
        {
            services.Error.WriteLine(ex.Message);
            return ExitCodes.Error;
        }
    }
}

internal static class PlanPrinter
{
    public static void Print(ChangePlan plan, TextWriter output)
    {
        output.WriteLine(plan.Description);
        foreach (var change in PlanSummary.Summarize(plan))
        {
            output.WriteLine($"  {change.Title}{(change.Note.Length == 0 ? string.Empty : $" ({change.Note})")}");
            foreach (var line in change.Lines)
            {
                output.WriteLine($"    {(line.Kind == DiffKind.Added ? '+' : '-')} {Format.Entry(line.Text)}");
            }
        }

        if (plan.RequiresElevation)
        {
            output.WriteLine("  (requires administrator rights)");
        }
    }
}

internal static class Format
{
    public static string Kind(EnvValueKind kind) => kind.RegistryName();

    public static string Entry(string raw) => raw.Trim().Length == 0 ? "(empty)" : raw;
}
