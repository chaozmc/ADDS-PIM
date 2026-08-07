namespace ADDS.PIM.Infrastructure.Worker;

/// <summary>
/// Non-secret API-to-Worker trust configuration. The client private key itself
/// remains in LocalMachine\My and is never represented here.
/// </summary>
public sealed class WorkerClientOptions
{
    public const string SectionName = "WorkerClient";
    internal const string CommandPath = "/internal/v1/temporary-group-memberships";

    public string? Endpoint { get; init; }
    public string? ClientCertificateThumbprint { get; init; }
    public string[] ExpectedServerCertificateThumbprints { get; init; } = [];

    public Uri GetEndpoint()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint)
            || !StringComparer.OrdinalIgnoreCase.Equals(endpoint.Scheme, Uri.UriSchemeHttps)
            || !StringComparer.Ordinal.Equals(endpoint.AbsolutePath, CommandPath)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new InvalidOperationException($"WorkerClient:Endpoint must be an HTTPS URI ending in {CommandPath} without a query or fragment.");
        }

        return endpoint;
    }

    public IReadOnlySet<string> GetExpectedServerCertificateThumbprints()
    {
        var allowed = ExpectedServerCertificateThumbprints
            .Select(WorkerClientCertificateProvider.NormalizeThumbprint)
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0)
        {
            throw new InvalidOperationException("At least one Worker server certificate thumbprint must be configured.");
        }

        return allowed;
    }
}
