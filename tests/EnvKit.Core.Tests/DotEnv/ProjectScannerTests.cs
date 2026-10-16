using EnvKit.Core.DotEnv;
using EnvKit.Core.Plans;

namespace EnvKit.Core.Tests.DotEnv;

public sealed class ProjectScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EnvKit.Tests", Guid.NewGuid().ToString("N"));

    public ProjectScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Create(string relative, string content = "")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Project(string id) => $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><UserSecretsId>{{id}}</UserSecretsId></PropertyGroup>
        </Project>
        """;

    [Fact]
    public void Finds_dotenv_files_and_user_secrets_projects_and_skips_build_folders()
    {
        Create(".env");
        Create(@"api\.env.local");
        Create(@"api\.env.example");
        Create(@"api\Api.csproj", Project("api-1234"));
        Create(@"web\node_modules\pkg\.env");
        Create(@"api\bin\Debug\.env");
        Create(@"api\Other.csproj", "<Project />");
        Create("not.env.txt");

        var found = ProjectScanner.Scan(_root);

        Assert.Equal(
            [Path.Combine(_root, ".env"), Path.Combine(_root, @"api\.env.example"), Path.Combine(_root, @"api\.env.local")],
            found.Where(f => f.Format == FileFormat.DotEnv).Select(f => f.Path));
        Assert.True(found.Single(f => f.Path.EndsWith(".example", StringComparison.Ordinal)).IsTemplate);

        var secrets = Assert.Single(found, f => f.Format == FileFormat.UserSecrets);
        Assert.Equal("api-1234", secrets.UserSecretsId);
        Assert.Equal(Path.Combine(ProjectScanner.UserSecretsRoot, "api-1234", "secrets.json"), secrets.Path);
        Assert.EndsWith("Api.csproj", secrets.Project, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"..\..\Windows")]
    [InlineData("a/b")]
    [InlineData("a:b")]
    [InlineData("  ")]
    public void User_secrets_ids_that_could_escape_the_folder_are_refused(string id) =>
        Assert.Null(ProjectScanner.UserSecretsPath(id));

    [Fact]
    public void Malicious_project_does_not_produce_a_path_outside_user_secrets()
    {
        Create("Evil.csproj", Project(@"..\..\..\Startup"));

        Assert.Empty(ProjectScanner.Scan(_root));
    }

    [Fact]
    public void Git_warnings_apply_to_real_dotenv_files_only()
    {
        Create(".env");
        Create(".env.local");
        Create(".env.example");
        var git = new FakeGit
        {
            [".env"] = (Tracked: true, Ignored: false),
            [".env.local"] = (Tracked: false, Ignored: false),
            [".env.example"] = (Tracked: true, Ignored: false),
        };

        var found = ProjectScanner.Scan(_root, git).ToDictionary(f => Path.GetFileName(f.Path));

        Assert.Equal([SecretFileWarning.Committed], found[".env"].WarningList);
        Assert.Equal([SecretFileWarning.NotIgnored], found[".env.local"].WarningList);
        Assert.Empty(found[".env.example"].WarningList);
    }

    [Fact]
    public void Real_git_reports_ignored_and_untracked_files()
    {
        if (!GitAvailable())
        {
            return;
        }

        RunGit("init", "-q");
        Create(".gitignore", ".env\n");
        Create(".env");
        Create(".env.local");

        var git = new GitInspector();

        Assert.Equal((false, true), git.Inspect(Path.Combine(_root, ".env")));
        Assert.Equal((false, false), git.Inspect(Path.Combine(_root, ".env.local")));
        Assert.Null(git.Inspect(Path.Combine(Path.GetTempPath(), $"no-repo-{Guid.NewGuid():N}", ".env")));
    }

    private void RunGit(params string[] arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        process.WaitForExit();
    }

    private static bool GitAvailable()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "--version") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true })!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private sealed class FakeGit : Dictionary<string, (bool Tracked, bool Ignored)>, IGitInspector
    {
        public (bool Tracked, bool Ignored)? Inspect(string path) =>
            TryGetValue(Path.GetFileName(path), out var status) ? status : null;
    }
}
