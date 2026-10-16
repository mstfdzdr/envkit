using EnvKit.Core.Paths;

namespace EnvKit.Core.Tests.Paths;

public class PathListTests
{
    [Theory]
    [InlineData(@"C:\a;C:\b", new[] { @"C:\a", @"C:\b" })]
    [InlineData(@"C:\a;;C:\b;", new[] { @"C:\a", "", @"C:\b", "" })]
    [InlineData(@"""C:\a;b"";C:\c", new[] { @"""C:\a;b""", @"C:\c" })]
    [InlineData("", new string[0])]
    public void Splits_on_semicolons_outside_quotes(string value, string[] expected) =>
        Assert.Equal(expected, PathList.Split(value));

    [Theory]
    [InlineData(@"C:\a;;C:\b;")]
    [InlineData(@"""C:\a;b"";C:\c")]
    [InlineData(@"%SystemRoot%\system32; C:\spaced ")]
    public void Split_then_join_round_trips(string value) =>
        Assert.Equal(value, PathList.Join(PathList.Split(value)));

    [Theory]
    [InlineData(@"C:\tools\", @"C:\tools")]
    [InlineData(@"C:\tools\\", @"C:\tools")]
    [InlineData(@"C:/tools/", @"C:\tools")]
    [InlineData(@"""C:\tools""", @"C:\tools")]
    [InlineData(@"  C:\tools  ", @"C:\tools")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"\\server\share\", @"\\server\share")]
    public void Normalizes_for_comparison(string input, string expected) =>
        Assert.Equal(expected, PathList.Normalize(input));

    [Theory]
    [InlineData(@"C:\x", true)]
    [InlineData(@"C:\", true)]
    [InlineData(@"\\server\share", true)]
    [InlineData("bin", false)]
    [InlineData(@"C:x", false)]
    [InlineData(@"\x", false)]
    [InlineData(".", false)]
    public void Detects_fully_qualified_paths(string path, bool expected) =>
        Assert.Equal(expected, PathList.IsFullyQualified(path));
}
