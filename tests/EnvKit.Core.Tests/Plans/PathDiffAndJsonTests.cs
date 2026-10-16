using EnvKit.Core.Plans;
using EnvKit.Testing;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Tests.Plans;

public class PathDiffTests
{
    private static string Render(string? before, string? after) =>
        string.Join(" ", PathDiff.Compute(before, after).Select(l => l.Kind switch
        {
            DiffKind.Added => "+" + l.Text,
            DiffKind.Removed => "-" + l.Text,
            _ => l.Text,
        }));

    [Theory]
    [InlineData("a;b;c", "a;b;c", "a b c")]
    [InlineData("a;c", "a;b;c", "a +b c")]
    [InlineData("a;b;b;c", "a;b;c", "a b -b c")]
    [InlineData("a;b", "b;a", "-a b +a")]
    [InlineData(null, "a", "+a")]
    [InlineData("a", null, "-a")]
    public void Diffs_entries(string? before, string? after, string expected) =>
        Assert.Equal(expected, Render(before, after));
}

public class PlanJsonTests
{
    [Fact]
    public void Plan_round_trips()
    {
        var store = new InMemoryEnvStore();
        store.Set(new EnvVariable("Path", @"%SystemRoot%", EnvScope.Machine, EnvValueKind.Expandable));
        var plan = new PlanBuilder(store)
            .Set("Path", EnvScope.Machine, @"%SystemRoot%;C:\tools")
            .Set("NEW", EnvScope.User, "1")
            .Build("round trip");

        var json = PlanJson.Serialize(plan);
        var copy = PlanJson.Deserialize(json);

        Assert.Equal(plan.Id, copy.Id);
        Assert.Equal(plan.Changes, copy.Changes);
        Assert.Contains("\"$type\": \"variable\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Expandable\"", json, StringComparison.Ordinal);
    }
}
