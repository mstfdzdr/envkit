using System.Text.Json;
using System.Text.Json.Serialization;
using EnvKit.Core.Vault;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Profiles;

/// <summary>One step of a profile. Values may use <c>%VAR%</c> and <c>${vault:NAME}</c>.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
[JsonDerivedType(typeof(SetVariableAction), "set")]
[JsonDerivedType(typeof(UnsetVariableAction), "unset")]
[JsonDerivedType(typeof(PrependPathAction), "prependPath")]
[JsonDerivedType(typeof(AppendPathAction), "appendPath")]
[JsonDerivedType(typeof(RemovePathAction), "removePath")]
public abstract record ProfileAction
{
    public abstract string Describe();
}

public sealed record SetVariableAction(string Name, string Value) : ProfileAction
{
    public override string Describe() => $"set {Name} = {Value}";
}

public sealed record UnsetVariableAction(string Name) : ProfileAction
{
    public override string Describe() => $"unset {Name}";
}

/// <summary>Puts the directory first in PATH, moving it there if it is already present.</summary>
public sealed record PrependPathAction(string Directory) : ProfileAction
{
    public override string Describe() => $"PATH: prepend {Directory}";
}

/// <summary>Adds the directory at the end of PATH unless it is already present.</summary>
public sealed record AppendPathAction(string Directory) : ProfileAction
{
    public override string Describe() => $"PATH: append {Directory}";
}

public sealed record RemovePathAction(string Directory) : ProfileAction
{
    public override string Describe() => $"PATH: remove {Directory}";
}

/// <summary>
/// A named set of environment changes, e.g. "java21": set JAVA_HOME and put %JAVA_HOME%\bin
/// first in PATH. Applied persistently (user scope) or only to one process (<c>envkit run</c>).
/// </summary>
public sealed record Profile(string Name, IReadOnlyList<ProfileAction> Actions, string? Description = null)
{
    /// <summary>Profiles referencing vault secrets can only be used with <c>envkit run</c>.</summary>
    [JsonIgnore]
    public bool UsesVault => Actions.OfType<SetVariableAction>().Any(a => VaultReference.HasReferences(a.Value));

    public void Validate()
    {
        VaultNames.Validate(Name);
        foreach (var action in Actions)
        {
            switch (action)
            {
                case SetVariableAction set:
                    EnvVariable.ValidateName(set.Name);
                    ArgumentNullException.ThrowIfNull(set.Value, nameof(set.Value));
                    break;
                case UnsetVariableAction unset:
                    EnvVariable.ValidateName(unset.Name);
                    break;
                default:
                    ArgumentException.ThrowIfNullOrWhiteSpace(PathDirectory(action), "directory");
                    break;
            }
        }
    }

    internal static string? PathDirectory(ProfileAction action) => action switch
    {
        PrependPathAction p => p.Directory,
        AppendPathAction a => a.Directory,
        RemovePathAction r => r.Directory,
        _ => null,
    };
}

public static class ProfileJson
{
    /// <summary>Profile files are meant to be written by hand, so "action" may come anywhere in an object.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        AllowOutOfOrderMetadataProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(Profile profile) => JsonSerializer.Serialize(profile, Options);

    /// <exception cref="JsonException">Not a profile.</exception>
    /// <exception cref="ArgumentException">The profile is invalid.</exception>
    public static Profile Deserialize(string json)
    {
        var profile = JsonSerializer.Deserialize<Profile>(json, Options) ?? throw new JsonException("Empty profile.");
        if (profile.Actions is null)
        {
            throw new JsonException("A profile needs an \"actions\" list.");
        }

        profile.Validate();
        return profile;
    }
}
