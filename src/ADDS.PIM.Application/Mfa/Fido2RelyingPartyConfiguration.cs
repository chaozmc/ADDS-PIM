namespace ADDS.PIM.Application.Mfa;

/// <summary>
/// Exact WebAuthn relying-party configuration. The origin is not inferred from
/// an incoming HTTP request because a proxy Host header is not trust evidence.
/// </summary>
public sealed record Fido2RelyingPartyConfiguration(string RelyingPartyId, string Origin)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RelyingPartyId)
            || RelyingPartyId.Contains("//", StringComparison.Ordinal)
            || RelyingPartyId.Contains('/', StringComparison.Ordinal)
            || RelyingPartyId.Contains(':', StringComparison.Ordinal)
            || !Uri.CheckHostName(RelyingPartyId).Equals(UriHostNameType.Dns))
        {
            throw new InvalidOperationException("FIDO2 relying-party ID must be a DNS host name.");
        }

        if (!Uri.TryCreate(Origin, UriKind.Absolute, out var origin)
            || !StringComparer.OrdinalIgnoreCase.Equals(origin.Scheme, Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment)
            || !StringComparer.Ordinal.Equals(origin.AbsolutePath, "/")
            || !StringComparer.OrdinalIgnoreCase.Equals(origin.Host, RelyingPartyId))
        {
            throw new InvalidOperationException("FIDO2 origin must be an exact HTTPS origin for the configured relying-party ID.");
        }
    }
}
