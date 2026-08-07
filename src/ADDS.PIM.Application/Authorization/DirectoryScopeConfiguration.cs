namespace ADDS.PIM.Application.Authorization;

/// <summary>
/// Non-secret installation configuration for the one operational directory
/// scope supported by the MVP. It deliberately contains no forest fingerprint;
/// a move to another forest requires a new scope ID and explicit reprovisioning.
/// </summary>
public sealed record DirectoryScopeConfiguration(
    Guid DirectoryScopeId,
    string DomainDnsName,
    string ForestDnsName)
{
    public void Validate()
    {
        if (DirectoryScopeId == Guid.Empty
            || !IsDnsName(DomainDnsName)
            || !IsDnsName(ForestDnsName))
        {
            throw new InvalidOperationException("Directory scope configuration is incomplete or invalid.");
        }
    }

    private static bool IsDnsName(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && Uri.CheckHostName(value.Trim()) == UriHostNameType.Dns;
}
