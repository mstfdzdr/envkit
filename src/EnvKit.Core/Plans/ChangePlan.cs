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

    public override string Subject => $@"{Scope}\{Name}";
}

/// <summary>Credential Manager is per user, so these never need elevation.</summary>
/// <param name="Before"><see langword="null"/> when the credential is being created.</param>
/// <param name="After"><see langword="null"/> when the credential is being deleted.</param>
public sealed record CredentialChange(CredentialKey Key, CredentialState? Before, CredentialState? After) : PlanChange
{
    public override bool RequiresElevation => false;

    public override string Subject => $"Credential {Key}";

    /// <summary>
    /// Deleting a domain credential cannot be undone: Windows never returns its secret, so
    /// neither the snapshot nor a restore can recreate it.
    /// </summary>
    [JsonIgnore]
    public bool IsIrreversible => Before is { Secret: null };
}

public sealed record ChangePlan(Guid Id, string Description, DateTimeOffset CreatedAt, IReadOnlyList<PlanChange> Changes)
{
    [JsonIgnore]
    public bool IsEmpty => Changes.Count == 0;

    [JsonIgnore]
    public bool RequiresElevation => Changes.Any(c => c.RequiresElevation);
}
