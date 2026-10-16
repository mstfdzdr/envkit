using System.Text.Json.Serialization;
using EnvKit.Core.Credentials;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Plans;

/// <summary>
/// One change, recorded as before/after state. The before state doubles as the precondition
/// checked when the plan is applied, and the pair is what diff views render.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(VariableChange), "variable")]
[JsonDerivedType(typeof(CredentialChange), "credential")]
[JsonDerivedType(typeof(FileChange), "file")]
public abstract record PlanChange
{
    [JsonIgnore]
    public abstract bool RequiresElevation { get; }

    /// <summary>What the change is about, e.g. <c>User\Path</c>; never includes a value.</summary>
    [JsonIgnore]
    public abstract string Subject { get; }
}

public sealed record VariableValue(string Value, EnvValueKind Kind);

/// <param name="Before"><see langword="null"/> when the variable is being created.</param>
/// <param name="After"><see langword="null"/> when the variable is being deleted.</param>
public sealed record VariableChange(EnvScope Scope, string Name, VariableValue? Before, VariableValue? After) : PlanChange
{
    public override bool RequiresElevation => Scope == EnvScope.Machine;

    public override string Subject => $@"{CoreText.Scope(Scope)}\{Name}";
}

/// <summary>Credential Manager is per user, so these never need elevation.</summary>
/// <param name="Before"><see langword="null"/> when the credential is being created.</param>
/// <param name="After"><see langword="null"/> when the credential is being deleted.</param>
public sealed record CredentialChange(CredentialKey Key, CredentialState? Before, CredentialState? After) : PlanChange
{
    public override bool RequiresElevation => false;

    public override string Subject => CoreText.Format("Subject_Credential", Key);

    /// <summary>
    /// Deleting a domain credential cannot be undone: Windows never returns its secret, so
    /// neither the snapshot nor a restore can recreate it.
    /// </summary>
    [JsonIgnore]
    public bool IsIrreversible => Before is { Secret: null };
}

public enum FileFormat
{
    DotEnv,
    UserSecrets,
}

/// <summary>
/// A whole-file change. The before bytes are the precondition, so an edit made by another
/// program after the preview stops the plan instead of being overwritten.
/// </summary>
/// <param name="Before"><see langword="null"/> when the file is being created.</param>
/// <param name="After"><see langword="null"/> when the file is being deleted.</param>
public sealed record FileChange(string Path, FileFormat Format, byte[]? Before, byte[]? After) : PlanChange
{
    public override bool RequiresElevation => false;

    public override string Subject => CoreText.Format(Format == FileFormat.DotEnv ? "Subject_DotEnv" : "Subject_UserSecrets", Path);

    public bool Equals(FileChange? other) =>
        other is not null && Path == other.Path && Format == other.Format && SameBytes(Before, other.Before) && SameBytes(After, other.After);

    public override int GetHashCode() => HashCode.Combine(Path, Format, Before?.Length, After?.Length);

    internal static bool SameBytes(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);
}

public sealed record ChangePlan(Guid Id, string Description, DateTimeOffset CreatedAt, IReadOnlyList<PlanChange> Changes)
{
    [JsonIgnore]
    public bool IsEmpty => Changes.Count == 0;

    [JsonIgnore]
    public bool RequiresElevation => Changes.Any(c => c.RequiresElevation);
}
