using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using EnvKit.Core.Plans;

namespace EnvKit.Windows.Elevation;

/// <summary>Relaunches <paramref name="helperPath"/> through UAC to apply machine-scope changes.</summary>
/// <param name="helperPath">An EnvKit executable that handles <see cref="ElevatedPlanHost.Verb"/>; usually the current one.</param>
/// <param name="plansDirectory">Where plan files wait for the helper; must be private to the user.</param>
public sealed class WindowsElevation(string helperPath, string plansDirectory) : IElevation
{
    private const int ErrorCancelled = 1223;
    private static readonly TimeSpan HelperTimeout = TimeSpan.FromMinutes(2);

    public static string DefaultPlansDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EnvKit", "plans");

    public static WindowsElevation ForCurrentProcess() =>
        new(Environment.ProcessPath ?? throw new InvalidOperationException("Unknown process path."), DefaultPlansDirectory);

    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public void ApplyElevated(ChangePlan machinePlan)
    {
        ArgumentNullException.ThrowIfNull(machinePlan);
        Directory.CreateDirectory(plansDirectory);
        var planFile = Path.Combine(plansDirectory, $"{machinePlan.Id:N}.json");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(machinePlan, PlanJson.Options);
        File.WriteAllBytes(planFile, bytes);

        try
        {
            var exitCode = RunHelper(ElevatedPlanHost.Arguments(planFile, ElevatedPlanHost.Hash(bytes)));
            switch (exitCode)
            {
                case ElevatedPlanHost.Applied:
                    return;
                case ElevatedPlanHost.Conflict:
                    throw new PlanConflictException("Machine variables changed while waiting for confirmation.");
                case ElevatedPlanHost.NotElevated:
                    throw new ElevatedApplyException("The helper ran without administrator rights.");
                case ElevatedPlanHost.HashMismatch:
                    throw new ElevatedApplyException("The plan file was modified before the helper read it; nothing was applied.");
                default:
                    throw new ElevatedApplyException($"The elevated helper failed (exit code {exitCode}); it reverted its changes.");
            }
        }
        finally
        {
            File.Delete(planFile);
        }
    }

    private int RunHelper(string arguments)
    {
        var startInfo = new ProcessStartInfo(helperPath)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = arguments,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw new OperationCanceledException("The administrator prompt was declined; nothing was changed.", ex);
        }

        using (process)
        {
            if (process is null)
            {
                throw new ElevatedApplyException("The elevated helper did not start.");
            }

            if (!process.WaitForExit(HelperTimeout))
            {
                throw new ElevatedApplyException("The elevated helper did not finish in time.");
            }

            return process.ExitCode;
        }
    }
}
