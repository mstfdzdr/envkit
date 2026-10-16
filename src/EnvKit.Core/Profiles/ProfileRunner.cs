using EnvKit.Core.Paths;
using EnvKit.Core.Plans;
using EnvKit.Core.Variables;
using EnvKit.Core.Vault;

namespace EnvKit.Core.Profiles;

public static class ProfilePlans
{
    /// <summary>
    /// Adds a profile's actions to a plan for the user scope. Values keep their <c>%VAR%</c>
    /// references (stored as REG_EXPAND_SZ), so "%JAVA_HOME%\bin" follows JAVA_HOME.
    /// </summary>
    /// <exception cref="InvalidOperationException">The profile uses vault secrets, which are never persisted.</exception>
    public static PlanBuilder Apply(this PlanBuilder builder, Profile profile)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.UsesVault)
        {
            throw new InvalidOperationException(
                $"Profile '{profile.Name}' uses vault secrets, which EnvKit never writes to the persistent environment. Use: envkit run --profile {profile.Name} -- <command>");
        }

        const EnvScope scope = EnvScope.User;
        foreach (var action in profile.Actions)
        {
            switch (action)
            {
                case SetVariableAction set:
                    builder.Set(set.Name, scope, set.Value);
                    break;
                case UnsetVariableAction unset:
                    builder.Delete(unset.Name, scope);
                    break;
                case PrependPathAction prepend:
                    builder.RemovePathEntry(prepend.Directory, scope);
                    builder.AddPathEntry(prepend.Directory, scope, prepend: true);
                    break;
                case AppendPathAction append:
                    builder.AddPathEntry(append.Directory, scope);
                    break;
                case RemovePathAction remove:
                    builder.RemovePathEntry(remove.Directory, scope);
                    break;
            }
        }

        return builder;
    }
}

/// <summary>Computes the environment block for one process: <c>envkit run</c>.</summary>
public static class ProfileEnvironment
{
    public const string PathName = "Path";

    /// <summary>
    /// Applies profiles in order to a copy of <paramref name="baseEnvironment"/>. Each value is
    /// expanded against the environment built so far, then vault references are filled in
    /// (in that order, so a secret containing '%' is never expanded).
    /// </summary>
    /// <exception cref="KeyNotFoundException">A referenced secret is not in the vault.</exception>
    public static Dictionary<string, string> Apply(IReadOnlyDictionary<string, string> baseEnvironment, IEnumerable<Profile> profiles, IVault vault)
    {
        ArgumentNullException.ThrowIfNull(baseEnvironment);
        ArgumentNullException.ThrowIfNull(profiles);
        var environment = new Dictionary<string, string>(baseEnvironment, EnvVariable.NameComparer);

        string Resolve(string value) =>
            VaultReference.Resolve(EnvExpander.Expand(value, name => environment.GetValueOrDefault(name)).Value, vault);

        List<string> PathEntries() => [.. PathList.Split(environment.GetValueOrDefault(PathName) ?? string.Empty)];

        bool Same(string entry, string directory) =>
            string.Equals(PathList.Normalize(entry), PathList.Normalize(directory), StringComparison.OrdinalIgnoreCase);

        foreach (var action in profiles.SelectMany(p => p.Actions))
        {
            switch (action)
            {
                case SetVariableAction set:
                    environment[set.Name] = Resolve(set.Value);
                    break;
                case UnsetVariableAction unset:
                    environment.Remove(unset.Name);
                    break;
                case PrependPathAction prepend:
                {
                    var directory = Resolve(prepend.Directory);
                    var entries = PathEntries();
                    entries.RemoveAll(e => Same(e, directory));
                    entries.Insert(0, directory);
                    environment[PathName] = PathList.Join(entries);
                    break;
                }

                case AppendPathAction append:
                {
                    var directory = Resolve(append.Directory);
                    var entries = PathEntries();
                    if (!entries.Any(e => Same(e, directory)))
                    {
                        entries.Add(directory);
                        environment[PathName] = PathList.Join(entries);
                    }

                    break;
                }

                case RemovePathAction remove:
                {
                    var directory = Resolve(remove.Directory);
                    var entries = PathEntries();
                    if (entries.RemoveAll(e => Same(e, directory)) > 0)
                    {
                        environment[PathName] = PathList.Join(entries);
                    }

                    break;
                }
            }
        }

        return environment;
    }

    /// <summary>The first match for <paramref name="command"/> on <paramref name="environment"/>'s PATH, using its PATHEXT.</summary>
    public static string? FindExecutable(string command, IReadOnlyDictionary<string, string> environment, Func<string, bool> fileExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(fileExists);

        // A path ("..\tool.exe", "C:\x\y.cmd") is used as given.
        if (command.IndexOfAny(['\\', '/', ':']) >= 0)
        {
            return command;
        }

        var extensions = (environment.GetValueOrDefault("PATHEXT") ?? CommandResolver.DefaultPathExt)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hasExtension = extensions.Contains(Path.GetExtension(command), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in PathList.Split(environment.GetValueOrDefault(PathName) ?? string.Empty))
        {
            var directory = PathList.Normalize(entry);
            if (!PathList.IsFullyQualified(directory))
            {
                continue;
            }

            var candidates = hasExtension ? [command] : extensions.Select(e => command + e);
            foreach (var candidate in candidates)
            {
                var full = directory.EndsWith('\\') ? directory + candidate : $@"{directory}\{candidate}";
                if (fileExists(full))
                {
                    return full;
                }
            }
        }

        return null;
    }
}
