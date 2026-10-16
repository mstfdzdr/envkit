using System.Text.Json;
using EnvKit.Core.Plans;
using EnvKit.Core.Profiles;
using EnvKit.Core.Variables;
using EnvKit.Testing;

namespace EnvKit.Core.Tests.Profiles;

public sealed class ProfileTests : IDisposable
{
    private static readonly Profile Java21 = new(
        "java21",
        [new SetVariableAction("JAVA_HOME", @"C:\jdk21"), new PrependPathAction(@"%JAVA_HOME%\bin")],
        "Temurin 21");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "EnvKit.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Json_round_trips_and_accepts_hand_written_files()
    {
        Assert.Equal(Java21.Actions, ProfileJson.Deserialize(ProfileJson.Serialize(Java21)).Actions);

        // "action" after the other properties, as people tend to write it.
        var handWritten = ProfileJson.Deserialize("""
            { "name": "node", "actions": [ { "directory": "C:\\nodejs", "action": "appendPath" } ] }
            """);
        Assert.Equal(new AppendPathAction(@"C:\nodejs"), Assert.Single(handWritten.Actions));
    }

    [Theory]
    [InlineData("""{ "name": "bad name", "actions": [] }""")]
    [InlineData("""{ "name": "x", "actions": [ { "action": "set", "name": "A=B", "value": "1" } ] }""")]
    [InlineData("""{ "name": "x", "actions": [ { "action": "prependPath", "directory": " " } ] }""")]
    public void Invalid_profiles_are_rejected(string json) =>
        Assert.ThrowsAny<ArgumentException>(() => ProfileJson.Deserialize(json));

    [Theory]
    [InlineData("""{ "name": "x" }""")]
    [InlineData("""{ "name": "x", "actions": [ { "action": "explode" } ] }""")]
    [InlineData("not json")]
    public void Malformed_profiles_are_json_errors(string json) =>
        Assert.Throws<JsonException>(() => ProfileJson.Deserialize(json));

    [Fact]
    public void File_store_saves_lists_and_skips_broken_files()
    {
        var store = new FileProfileStore(_directory);
        store.Save(Java21);
        File.WriteAllText(Path.Combine(_directory, "broken.json"), "{");

        Assert.Equal(["java21"], store.List().Select(p => p.Name));
        Assert.Equal(Java21.Actions, store.Get("JAVA21")!.Actions);
        Assert.True(store.Delete("java21"));
        Assert.Null(store.Get("java21"));
        Assert.Throws<ArgumentException>(() => store.Get(@"..\secrets"));
    }

    [Fact]
    public void Applying_persistently_keeps_references_and_switching_only_changes_the_home()
    {
        var env = new InMemoryEnvStore();
        env.Set(new EnvVariable("Path", @"C:\tools", EnvScope.User));
        var applier = new PlanApplier(env, null, null);

        applier.Apply(new PlanBuilder(env).Apply(Java21).Build("java21"));

        Assert.Equal(@"%JAVA_HOME%\bin;C:\tools", env.Get("Path", EnvScope.User)!.Value);
        Assert.Equal(EnvValueKind.Expandable, env.Get("Path", EnvScope.User)!.Kind);

        var java17 = Java21 with { Name = "java17", Actions = [new SetVariableAction("JAVA_HOME", @"C:\jdk17"), new PrependPathAction(@"%JAVA_HOME%\bin")] };
        var change = Assert.IsType<VariableChange>(Assert.Single(new PlanBuilder(env).Apply(java17).Build("switch").Changes));

        Assert.Equal("JAVA_HOME", change.Name);
    }

    [Fact]
    public void Prepending_moves_an_existing_entry_to_the_front()
    {
        var env = new InMemoryEnvStore();
        env.Set(new EnvVariable("Path", @"C:\a;C:\node;C:\b", EnvScope.User));
        var profile = new Profile("node", [new PrependPathAction(@"C:\node\"), new RemovePathAction(@"C:\b")]);

        new PlanApplier(env, null, null).Apply(new PlanBuilder(env).Apply(profile).Build("node"));

        Assert.Equal(@"C:\node\;C:\a", env.Get("Path", EnvScope.User)!.Value);
    }

    [Fact]
    public void Profiles_with_vault_secrets_cannot_be_applied_persistently()
    {
        var profile = new Profile("api", [new SetVariableAction("OPENAI_API_KEY", "${vault:openai}")]);

        Assert.True(profile.UsesVault);
        var error = Assert.Throws<InvalidOperationException>(() => new PlanBuilder(new InMemoryEnvStore()).Apply(profile));
        Assert.Contains("envkit run --profile api", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Process_environment_applies_profiles_in_order_with_expansion_and_secrets()
    {
        var vault = new InMemoryVault();
        vault.Set("openai", "sk-%NOT_A_VAR%");
        var baseEnv = new Dictionary<string, string> { ["PATH"] = @"C:\Windows;C:\jdk17\bin", ["TEMP"] = @"C:\t", ["UNWANTED"] = "x" };
        var api = new Profile("api", [new SetVariableAction("OPENAI_API_KEY", "${vault:openai}"), new UnsetVariableAction("unwanted")]);

        var env = ProfileEnvironment.Apply(baseEnv, [Java21 with { Actions = [.. Java21.Actions, new RemovePathAction(@"C:\jdk17\bin")] }, api], vault);

        Assert.Equal(@"C:\jdk21", env["JAVA_HOME"]);
        Assert.Equal(@"C:\jdk21\bin;C:\Windows", env["Path"]);
        Assert.Equal("sk-%NOT_A_VAR%", env["OPENAI_API_KEY"]);
        Assert.False(env.ContainsKey("UNWANTED"));
        Assert.Equal(@"C:\t", env["temp"]);
        Assert.Equal(@"C:\Windows;C:\jdk17\bin", baseEnv["PATH"]);
    }

    [Fact]
    public void Missing_secret_stops_the_run() =>
        Assert.Throws<KeyNotFoundException>(() => ProfileEnvironment.Apply(
            new Dictionary<string, string>(),
            [new Profile("api", [new SetVariableAction("K", "${vault:nope}")])],
            new InMemoryVault()));

    [Fact]
    public void Executables_are_found_on_the_new_path()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\jdk21\bin\java.exe", @"C:\tools\build.cmd" };
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Path"] = @"relative;C:\jdk21\bin\;C:\tools",
            ["PATHEXT"] = ".EXE;.CMD",
        };

        // Windows paths are case-insensitive; the extension comes back as PATHEXT spells it.
        Assert.Equal(@"C:\jdk21\bin\java.exe", ProfileEnvironment.FindExecutable("java", env, files.Contains), ignoreCase: true);
        Assert.Equal(@"C:\tools\build.cmd", ProfileEnvironment.FindExecutable("build.cmd", env, files.Contains));
        Assert.Equal(@".\local.exe", ProfileEnvironment.FindExecutable(@".\local.exe", env, files.Contains));
        Assert.Null(ProfileEnvironment.FindExecutable("nope", env, files.Contains));
    }
}
