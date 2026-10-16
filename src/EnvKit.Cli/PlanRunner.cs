using EnvKit.Core;
using EnvKit.Core.Paths;
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
            var result = services.Applier().Apply(plan);
            output.WriteLine($"Applied. Undo with: envkit snapshot restore {result.Snapshot!.Id}");
            output.WriteLine(result.Broadcast
                ? "New terminals see the change; terminals that are already open keep their old environment."
                : "Running programs could not be notified; sign out and back in to pick up the change.");
            return ExitCodes.Ok;
        }
        catch (ElevationRequiredException)
        {
            services.Error.WriteLine("Machine-scope changes need administrator rights. Nothing was changed.");
            services.Error.WriteLine("Run the command again from an elevated terminal.");
            return ExitCodes.ElevationRequired;
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
        foreach (var change in plan.Changes.Cast<VariableChange>())
        {
            output.WriteLine($@"  {change.Scope}\{change.Name}{Header(change)}");
            if (EnvVariable.NameComparer.Equals(change.Name, PathAnalyzer.VariableName))
            {
                foreach (var line in PathDiff.Compute(change.Before?.Value, change.After?.Value))
                {
                    if (line.Kind != DiffKind.Unchanged)
                    {
                        output.WriteLine($"    {(line.Kind == DiffKind.Added ? '+' : '-')} {Format.Entry(line.Text)}");
                    }
                }
            }
            else
            {
                if (change.Before is { } before)
                {
                    output.WriteLine($"    - {before.Value}");
                }

                if (change.After is { } after)
                {
                    output.WriteLine($"    + {after.Value}");
                }
            }
        }

        if (plan.RequiresElevation)
        {
            output.WriteLine("  (requires administrator rights)");
        }
    }

    private static string Header(VariableChange change) => (change.Before, change.After) switch
    {
        (null, _) => " (new)",
        (_, null) => " (delete)",
        ({ } b, { } a) when b.Kind != a.Kind => $" ({Format.Kind(b.Kind)} -> {Format.Kind(a.Kind)})",
        _ => string.Empty,
    };
}

internal static class Format
{
    public static string Kind(EnvValueKind kind) => kind == EnvValueKind.Expandable ? "REG_EXPAND_SZ" : "REG_SZ";

    public static string Entry(string raw) => raw.Trim().Length == 0 ? "(empty)" : raw;
}
