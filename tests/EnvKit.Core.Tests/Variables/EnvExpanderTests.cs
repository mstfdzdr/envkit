using EnvKit.Core.Tests.Fakes;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Tests.Variables;

public class EnvExpanderTests
{
    private static readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HOME"] = @"C:\Users\me",
        ["TOOLS"] = @"%HOME%\tools",
        ["LOOP_A"] = "%LOOP_B%",
        ["LOOP_B"] = "%LOOP_A%",
    };

    private static Expansion Expand(string value) => EnvExpander.Expand(value, Values.GetValueOrDefault);

    [Theory]
    [InlineData(@"%HOME%\bin", @"C:\Users\me\bin")]
    [InlineData(@"%home%\bin", @"C:\Users\me\bin")]
    [InlineData(@"%TOOLS%\git", @"C:\Users\me\tools\git")]
    [InlineData("no refs", "no refs")]
    [InlineData("100%", "100%")]
    [InlineData("%%", "%%")]
    public void Expands_known_references(string input, string expected)
    {
        var result = Expand(input);

        Assert.Equal(expected, result.Value);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Leaves_unknown_references_in_place_and_reports_them()
    {
        var result = Expand(@"%NOPE%\bin;%HOME%;%nope%");

        Assert.Equal(@"%NOPE%\bin;C:\Users\me;%nope%", result.Value);
        Assert.Equal(["NOPE"], result.Unresolved);
    }

    [Fact]
    public void Cycles_end_as_unresolved_instead_of_overflowing() =>
        Assert.False(Expand("%LOOP_A%").IsComplete);

    [Theory]
    [InlineData("%A%", true)]
    [InlineData(@"x\%A%\y", true)]
    [InlineData("%%", false)]
    [InlineData("50%", false)]
    [InlineData("%", false)]
    [InlineData("%%A%", false)]
    [InlineData("plain", false)]
    public void Detects_references(string value, bool expected) =>
        Assert.Equal(expected, EnvExpander.HasReferences(value));
}

public class EnvLookupTests
{
    private static EnvLookup CreateLookup()
    {
        var store = new InMemoryEnvStore();
        store.Set(new EnvVariable("SHARED", "machine", EnvScope.Machine));
        store.Set(new EnvVariable("SHARED", "user", EnvScope.User));
        store.Set(new EnvVariable("USER_ONLY", "u", EnvScope.User));
        var process = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["USER_ONLY"] = "u",
            ["SystemRoot"] = @"C:\Windows",
        };
        return new EnvLookup(store, process.GetValueOrDefault);
    }

    [Fact]
    public void User_scope_prefers_user_values() =>
        Assert.Equal("user", CreateLookup().Resolve("SHARED", EnvScope.User));

    [Fact]
    public void Machine_scope_cannot_see_user_only_variables_even_via_the_process() =>
        Assert.Null(CreateLookup().Resolve("USER_ONLY", EnvScope.Machine));

    [Fact]
    public void System_provided_values_resolve_in_both_scopes()
    {
        var lookup = CreateLookup();

        Assert.Equal(@"C:\Windows", lookup.Resolve("SystemRoot", EnvScope.Machine));
        Assert.Equal(@"C:\Windows", lookup.Resolve("SystemRoot", EnvScope.User));
    }
}
