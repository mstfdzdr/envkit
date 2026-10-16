using System.Text.Json;
using EnvKit.Core.Plans;

namespace EnvKit.Core.Credentials;

/// <summary>One row of an inventory export; deliberately has no secret field.</summary>
public sealed record CredentialSummary(
    string TargetName,
    CredentialKind Kind,
    CredentialGroup Group,
    string? UserName,
    CredentialPersistence Persistence,
    string? Comment,
    DateTimeOffset LastWritten);

public static class CredentialExport
{
    public static CredentialSummary Summarize(Credential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        return new CredentialSummary(
            credential.TargetName,
            credential.Kind,
            CredentialClassifier.Classify(credential.Key),
            credential.State.UserName,
            credential.State.Persistence,
            credential.State.Comment,
            credential.LastWritten);
    }

    public static string ToJson(IEnumerable<Credential> credentials) =>
        JsonSerializer.Serialize(credentials.Select(Summarize).ToList(), PlanJson.Options);
}
