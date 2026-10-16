using EnvKit.Core;
using EnvKit.Core.Plans;
using EnvKit.Core.Security;
using EnvKit.Core.Snapshots;
using EnvKit.Core.Variables;

namespace EnvKit.Testing;

/// <summary>Each reading is one second after the previous one.</summary>
public sealed class SteppingTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now = _now.AddSeconds(1);
}

public sealed class CountingBroadcaster : IEnvChangeBroadcaster
{
    public int Count { get; private set; }

    public bool Broadcast()
    {
        Count++;
        return true;
    }
}

public sealed class InMemorySnapshotStore : ISnapshotStore
{
    public List<Snapshot> Saved { get; } = [];

    public void Save(Snapshot snapshot) => Saved.Add(snapshot);

    public IReadOnlyList<SnapshotInfo> List() => [.. Saved.Select(s => s.Info).Reverse()];

    public Snapshot Load(string id) => Saved.Single(s => s.Id == id);
}

/// <summary>Reversible but visibly not plain text.</summary>
public sealed class XorProtector : ISecretProtector
{
    public int Calls { get; private set; }

    public byte[] Protect(byte[] data)
    {
        Calls++;
        return [.. data.Select(b => (byte)(b ^ 0x5A))];
    }

    public byte[] Unprotect(byte[] data)
    {
        Calls++;
        return [.. data.Select(b => (byte)(b ^ 0x5A))];
    }
}

/// <summary>
/// Stands in for the UAC helper: applies machine plans straight to <paramref name="helperStore"/>,
/// which should bypass any <see cref="MachineReadOnlyStore"/> the caller uses.
/// </summary>
public sealed class FakeElevation(IEnvStore helperStore) : IElevation
{
    public bool IsElevated { get; set; }

    public List<ChangePlan> Requests { get; } = [];

    /// <summary>Thrown instead of applying, e.g. <see cref="OperationCanceledException"/> for a declined prompt.</summary>
    public Exception? Failure { get; set; }

    public void ApplyElevated(ChangePlan machinePlan)
    {
        Requests.Add(machinePlan);
        if (Failure is not null)
        {
            throw Failure;
        }

        new PlanApplier(helperStore, snapshots: null, broadcaster: null).Apply(machinePlan);
    }
}

/// <summary>Behaves like a non-elevated process: machine writes are refused.</summary>
public sealed class MachineReadOnlyStore(IEnvStore inner) : IEnvStore
{
    public IReadOnlyList<EnvVariable> GetAll(EnvScope scope) => inner.GetAll(scope);

    public EnvVariable? Get(string name, EnvScope scope) => inner.Get(name, scope);

    public void Set(EnvVariable variable)
    {
        Guard(variable.Scope);
        inner.Set(variable);
    }

    public bool Delete(string name, EnvScope scope)
    {
        Guard(scope);
        return inner.Delete(name, scope);
    }

    private static void Guard(EnvScope scope)
    {
        if (scope == EnvScope.Machine)
        {
            throw new ElevationRequiredException();
        }
    }
}
