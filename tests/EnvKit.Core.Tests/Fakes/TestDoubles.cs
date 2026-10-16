using EnvKit.Core.Security;
using EnvKit.Core.Snapshots;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Tests.Fakes;

/// <summary>Each reading is one second after the previous one.</summary>
internal sealed class SteppingTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now = _now.AddSeconds(1);
}

internal sealed class CountingBroadcaster : IEnvChangeBroadcaster
{
    public int Count { get; private set; }

    public bool Broadcast()
    {
        Count++;
        return true;
    }
}

internal sealed class InMemorySnapshotStore : ISnapshotStore
{
    public List<Snapshot> Saved { get; } = [];

    public void Save(Snapshot snapshot) => Saved.Add(snapshot);

    public IReadOnlyList<SnapshotInfo> List() => [.. Saved.Select(s => s.Info).Reverse()];

    public Snapshot Load(string id) => Saved.Single(s => s.Id == id);
}

/// <summary>Reversible but visibly not plain text.</summary>
internal sealed class XorProtector : ISecretProtector
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

/// <summary>Behaves like a non-elevated process: machine writes are refused.</summary>
internal sealed class MachineReadOnlyStore(IEnvStore inner) : IEnvStore
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
