namespace EnvKit.Core.Credentials;

/// <summary>Which program a credential most likely belongs to, so they can be grouped and filtered.</summary>
public enum CredentialGroup
{
    Git,
    GitHub,
    VisualStudio,
    VSCode,
    Docker,
    RemoteDesktop,
    MicrosoftAccount,
    Office,
    Network,
    Other,
}

public static class CredentialClassifier
{
    // Checked in order; the first match wins. Patterns are matched case-insensitively.
    private static readonly (CredentialGroup Group, Func<string, bool> Matches)[] Rules =
    [
        (CredentialGroup.Git, t => t.StartsWith("git:", StringComparison.OrdinalIgnoreCase)),
        (CredentialGroup.GitHub, t => t.StartsWith("GitHub", StringComparison.OrdinalIgnoreCase)),
        (CredentialGroup.VSCode, t => t.StartsWith("vscode", StringComparison.OrdinalIgnoreCase)),
        (CredentialGroup.VisualStudio, t => Contains(t, "VisualStudio") || t.StartsWith("VSCredentials", StringComparison.OrdinalIgnoreCase)),
        (CredentialGroup.Docker, t => Contains(t, "docker")),
        (CredentialGroup.RemoteDesktop, t => t.StartsWith("TERMSRV/", StringComparison.OrdinalIgnoreCase)),
        (CredentialGroup.MicrosoftAccount, t => t.StartsWith("MicrosoftAccount:", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("WindowsLive:", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("SSO_POP_", StringComparison.OrdinalIgnoreCase)),
        (CredentialGroup.Office, t => t.StartsWith("MicrosoftOffice", StringComparison.OrdinalIgnoreCase) || Contains(t, "OneDrive")),
    ];

    public static CredentialGroup Classify(CredentialKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        // Windows logon credentials for file shares and other machines.
        if (key.Kind is CredentialKind.DomainPassword or CredentialKind.DomainCertificate
            or CredentialKind.DomainVisiblePassword or CredentialKind.DomainExtended)
        {
            return key.TargetName.StartsWith("TERMSRV/", StringComparison.OrdinalIgnoreCase)
                ? CredentialGroup.RemoteDesktop
                : CredentialGroup.Network;
        }

        foreach (var (group, matches) in Rules)
        {
            if (matches(key.TargetName))
            {
                return group;
            }
        }

        return CredentialGroup.Other;
    }

    private static bool Contains(string text, string fragment) => text.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}
