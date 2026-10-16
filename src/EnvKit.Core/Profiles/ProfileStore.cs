using System.Text.Json;

namespace EnvKit.Core.Profiles;

public interface IProfileStore
{
    IReadOnlyList<Profile> List();

    Profile? Get(string name);

    void Save(Profile profile);

    bool Delete(string name);
}

/// <summary>One readable JSON file per profile, so profiles can be shared or kept in a repository.</summary>
public sealed class FileProfileStore(string directory) : IProfileStore
{
    public static string DefaultDirectory => EnvKitPaths.Profiles;

    public IReadOnlyList<Profile> List()
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var profiles = new List<Profile>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                profiles.Add(ProfileJson.Deserialize(File.ReadAllText(file)));
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or IOException)
            {
                // A hand-edited file with a mistake should not hide the others.
            }
        }

        return [.. profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public Profile? Get(string name)
    {
        var path = PathFor(name);
        return File.Exists(path) ? ProfileJson.Deserialize(File.ReadAllText(path)) : null;
    }

    public void Save(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        Directory.CreateDirectory(directory);
        File.WriteAllText(PathFor(profile.Name), ProfileJson.Serialize(profile));
    }

    public bool Delete(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    // Names are restricted to file-safe characters (see Profile.Validate), so this cannot escape the directory.
    private string PathFor(string name)
    {
        Vault.VaultNames.Validate(name);
        return Path.Combine(directory, name + ".json");
    }
}
