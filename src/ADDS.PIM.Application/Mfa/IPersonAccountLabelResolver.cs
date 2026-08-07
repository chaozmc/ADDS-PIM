namespace ADDS.PIM.Application.Mfa;

/// <summary>
/// A human-recognizable label for a person, for display inside a WebAuthn/authenticator-app ceremony
/// only (e.g. the FIDO2 user name/display name shown by the security key's own management tool, or the
/// TOTP QR code's account label) - never used for authorization or identity resolution.
/// </summary>
public sealed record PersonAccountLabel(string DomainQualifiedName, string DisplayName)
{
    /// <summary>"{DomainQualifiedName} ({DisplayName})", e.g. "HOME\jdoe (John Doe)".</summary>
    public string Combined => $"{DomainQualifiedName} ({DisplayName})";
}

public interface IPersonAccountLabelResolver
{
    /// <summary>Null if the person has no currently active authenticating account link.</summary>
    Task<PersonAccountLabel?> ResolveAsync(Guid personId, CancellationToken cancellationToken);
}
