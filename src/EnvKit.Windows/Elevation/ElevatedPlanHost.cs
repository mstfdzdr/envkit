using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using EnvKit.Core;
using EnvKit.Core.Plans;
using EnvKit.Core.Variables;

namespace EnvKit.Windows.Elevation;

/// <summary>
/// Runs inside the elevated helper process (<c>apply-plan &lt;file&gt; --sha256 &lt;hash&gt;</c>).
/// It only reads: the outcome is reported through the exit code, because an elevated
/// process writing into a user-writable folder invites symlink tricks.
/// </summary>
public static class ElevatedPlanHost
{
    public const string Verb = "apply-plan";
    public const string HashOption = "--sha256";

    public const int Applied = 0;
    public const int Failed = 1;
    public const int InvalidPlan = 2;
    public const int HashMismatch = 3;
    public const int Conflict = 4;
    public const int NotElevated = 5;

    public static string Arguments(string planFile, string sha256) => $"{Verb} \"{planFile}\" {HashOption} {sha256}";

    public static string Hash(byte[] planBytes) => Convert.ToHexStringLower(SHA256.HashData(planBytes));

    public static int Run(string planFile, string expectedSha256, IEnvStore store)
    {
        ArgumentNullException.ThrowIfNull(planFile);
        ArgumentNullException.ThrowIfNull(expectedSha256);

        byte[] bytes;
        try
        {
            // Read once and verify that copy: the file stays writable by the unelevated user.
            bytes = File.ReadAllBytes(planFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return InvalidPlan;
        }

        var expected = new byte[SHA256.HashSizeInBytes];
        var parsed = Convert.FromHexString(expectedSha256, expected, out _, out var written);
        if (parsed != OperationStatus.Done
            || written != expected.Length
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), expected))
        {
            return HashMismatch;
        }

        ChangePlan plan;
        try
        {
            plan = JsonSerializer.Deserialize<ChangePlan>(bytes, PlanJson.Options) ?? throw new JsonException("Empty plan.");
        }
        catch (JsonException)
        {
            return InvalidPlan;
        }

        // User-scope writes here would land in the elevating account's HKCU, which may not be the user's.
        if (plan.Changes.Any(c => c is not VariableChange { Scope: EnvScope.Machine }))
        {
            return InvalidPlan;
        }

        try
        {
            new PlanApplier(store, snapshots: null, broadcaster: null).Apply(plan);
            return Applied;
        }
        catch (PlanConflictException)
        {
            return Conflict;
        }
        catch (ElevationRequiredException)
        {
            return NotElevated;
        }
        catch (Exception ex) when (ex is PlanApplyException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Failed;
        }
    }
}
