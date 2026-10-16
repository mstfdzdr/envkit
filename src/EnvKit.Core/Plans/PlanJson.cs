using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvKit.Core.Plans;

/// <summary>Serialization shared by plan files (elevated apply) and snapshots.</summary>
public static class PlanJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(ChangePlan plan) => JsonSerializer.Serialize(plan, Options);

    public static ChangePlan Deserialize(string json) =>
        JsonSerializer.Deserialize<ChangePlan>(json, Options) ?? throw new JsonException("Empty plan.");
}
