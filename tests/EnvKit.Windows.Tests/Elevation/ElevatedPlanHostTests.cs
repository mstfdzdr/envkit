using System.Text;
using System.Text.Json;
using EnvKit.Core.Plans;
using EnvKit.Core.Variables;
using EnvKit.Testing;
using EnvKit.Windows.Elevation;

namespace EnvKit.Windows.Tests.Elevation;

public sealed class ElevatedPlanHostTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "EnvKit.Tests", Guid.NewGuid().ToString("N"));
    private readonly InMemoryEnvStore _store = new();

    public ElevatedPlanHostTests()
    {
        Directory.CreateDirectory(_directory);
        _store.Set(new EnvVariable("M", "before", EnvScope.Machine));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private (string File, string Hash) Write(ChangePlan plan) => Write(JsonSerializer.SerializeToUtf8Bytes(plan, PlanJson.Options));

    private (string File, string Hash) Write(byte[] bytes)
    {
        var file = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllBytes(file, bytes);
        return (file, ElevatedPlanHost.Hash(bytes));
    }

    private ChangePlan MachinePlan() => new PlanBuilder(_store).Set("M", EnvScope.Machine, "after").Build("t");

    [Fact]
    public void Applies_a_verified_machine_plan()
    {
        var (file, hash) = Write(MachinePlan());

        Assert.Equal(ElevatedPlanHost.Applied, ElevatedPlanHost.Run(file, hash, _store));
        Assert.Equal("after", _store.Get("M", EnvScope.Machine)?.Value);
    }

    [Fact]
    public void Refuses_a_file_changed_after_hashing()
    {
        var (file, hash) = Write(MachinePlan());
        var tampered = new PlanBuilder(_store).Set("M", EnvScope.Machine, @"C:\evil").Build("t");
        File.WriteAllBytes(file, JsonSerializer.SerializeToUtf8Bytes(tampered, PlanJson.Options));

        Assert.Equal(ElevatedPlanHost.HashMismatch, ElevatedPlanHost.Run(file, hash, _store));
        Assert.Equal("before", _store.Get("M", EnvScope.Machine)?.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-hex")]
    [InlineData("abcd")]
    public void Refuses_malformed_hashes(string hash)
    {
        var (file, _) = Write(MachinePlan());

        Assert.Equal(ElevatedPlanHost.HashMismatch, ElevatedPlanHost.Run(file, hash, _store));
    }

    [Fact]
    public void Refuses_user_scope_changes()
    {
        var (file, hash) = Write(new PlanBuilder(_store).Set("U", EnvScope.User, "x").Build("t"));

        Assert.Equal(ElevatedPlanHost.InvalidPlan, ElevatedPlanHost.Run(file, hash, _store));
        Assert.Null(_store.Get("U", EnvScope.User));
    }

    [Fact]
    public void Reports_conflicts()
    {
        var (file, hash) = Write(MachinePlan());
        _store.Set(new EnvVariable("M", "changed meanwhile", EnvScope.Machine));

        Assert.Equal(ElevatedPlanHost.Conflict, ElevatedPlanHost.Run(file, hash, _store));
        Assert.Equal("changed meanwhile", _store.Get("M", EnvScope.Machine)?.Value);
    }

    [Fact]
    public void Refuses_garbage_and_missing_files()
    {
        var (file, hash) = Write(Encoding.UTF8.GetBytes("{ not json"));

        Assert.Equal(ElevatedPlanHost.InvalidPlan, ElevatedPlanHost.Run(file, hash, _store));
        Assert.Equal(ElevatedPlanHost.InvalidPlan, ElevatedPlanHost.Run(Path.Combine(_directory, "nope.json"), hash, _store));
    }
}
