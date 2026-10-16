using EnvKit.Core.Variables;

namespace EnvKit.Core.Tests.Variables;

public class EnvVariableTests
{
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("A=B")]
    [InlineData("A\0B")]
    public void Rejects_invalid_names(string name) =>
        Assert.ThrowsAny<ArgumentException>(() => new EnvVariable(name, "x", EnvScope.User));

    [Fact]
    public void Accepts_value_at_max_length()
    {
        var variable = new EnvVariable("X", new string('a', EnvVariable.MaxValueLength), EnvScope.User);

        Assert.Equal(EnvVariable.MaxValueLength, variable.Value.Length);
    }

    [Fact]
    public void Rejects_value_over_max_length() =>
        Assert.Throws<ArgumentException>(
            () => new EnvVariable("X", new string('a', EnvVariable.MaxValueLength + 1), EnvScope.User));

    [Fact]
    public void Defaults_to_literal_kind() =>
        Assert.Equal(EnvValueKind.Literal, new EnvVariable("X", "y", EnvScope.Machine).Kind);

    [Fact]
    public void Name_comparer_is_case_insensitive() =>
        Assert.Equal(0, EnvVariable.NameComparer.Compare("Path", "PATH"));
}
