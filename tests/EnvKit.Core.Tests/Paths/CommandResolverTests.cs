using EnvKit.Core.Paths;
using EnvKit.Testing;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Tests.Paths;

public class CommandResolverTests
{
    private const string WindowsApps = @"C:\Users\me\AppData\Local\Microsoft\WindowsApps";

    private readonly FakeFileSystem _fileSystem = new FakeFileSystem()
        .WithFiles(
            @"C:\Python312\python.exe",
            WindowsApps + @"\python.exe",
            @"C:\tools\build.cmd",
            @"C:\tools\build.exe",
            @"C:\Missing\never.exe");

    private (CommandResolver Resolver, PathReport Report) Setup(string machine, string user)
    {
        var store = new InMemoryEnvStore();
        store.Set(new EnvVariable("Path", machine, EnvScope.Machine));
        store.Set(new EnvVariable("Path", user, EnvScope.User));
        var report = new PathAnalyzer(_fileSystem).Analyze(store, new EnvLookup(store, _ => null));
        return (new CommandResolver(_fileSystem), report);
    }

    [Fact]
    public void First_match_wins_and_later_ones_are_shadowed()
    {
        var (resolver, report) = Setup(WindowsApps, @"C:\Python312");

        var matches = resolver.FindAll("python", report);

        Assert.Equal([WindowsApps + @"\python.exe", @"C:\Python312\python.exe"], matches.Select(m => m.FullPath));
        Assert.True(matches[0].IsAppExecutionAlias);
        Assert.False(matches[1].IsAppExecutionAlias);
    }

    [Fact]
    public void Within_a_directory_PATHEXT_order_decides()
    {
        var (resolver, report) = Setup(@"C:\tools", "");

        Assert.Equal(@"C:\tools\build.exe", resolver.FindAll("build", report, ".EXE;.CMD")[0].FullPath);
        Assert.Equal(@"C:\tools\build.cmd", resolver.FindAll("build", report, ".CMD;.EXE")[0].FullPath);
    }

    [Fact]
    public void Explicit_extension_is_looked_up_as_is()
    {
        var (resolver, report) = Setup(@"C:\tools", "");

        var match = Assert.Single(resolver.FindAll("build.cmd", report));
        Assert.Equal(@"C:\tools\build.cmd", match.FullPath);
    }

    [Fact]
    public void Duplicate_directories_are_searched_once()
    {
        var (resolver, report) = Setup(@"C:\tools", @"C:\TOOLS\");

        Assert.Single(resolver.FindAll("build.exe", report));
    }

    [Fact]
    public void Rejects_paths() =>
        Assert.Throws<ArgumentException>(() => Setup(@"C:\tools", "").Resolver.FindAll(@"C:\tools\build", Setup(@"C:\tools", "").Report));
}
