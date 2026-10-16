using System.Text.Json.Serialization;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Plans;

/// <summary>
/// One change, recorded as before/after state. The before state doubles as the precondition
/// checked when the plan is applied, and the pair is what diff views render.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(VariableChange), "variable")]
public abstract record PlanChange
{
    [JsonIgnore]
    public abstract bool RequiresElevation { get; }
}

public sealed record VariableValue(string Value, EnvValueKind Kind);

/// <param name="Before"><see langword="null"/> when the variable is being created.</param>
/// <param name="After"><see langword="null"/> when the variable is being deleted.</param>
public sealed record VariableChange(EnvScope Scope, string Name, VariableValue? Before, VariableValue? After) : PlanChange
{
    public override bool RequiresElevation => Scope == EnvScope.Machine;
}

public sealed record ChangePlan(Guid Id, string Description, DateTimeOffset CreatedAt, IReadOnlyList<PlanChange> Changes)
{
    [JsonIgnore]
    public bool IsEmpty => Changes.Count == 0;

    [JsonIgnore]
    public bool RequiresElevation => Changes.Any(c => c.RequiresElevation);
}
