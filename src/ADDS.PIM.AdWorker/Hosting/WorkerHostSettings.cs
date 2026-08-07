using System.Net;
using Microsoft.Win32;

namespace ADDS.PIM.AdWorker.Hosting;

public sealed record WorkerHostSettings(
    IPAddress BindAddress,
    int Port,
    string ServerCertificateThumbprint,
    string WorkerClientCertificateThumbprint,
    IReadOnlySet<string> AllowedApiClientCertificateThumbprints,
    string SqlConnectionString,
    string DomainController,
    int CommandMaxAgeSeconds)
{
    public const string RegistryPath = @"SOFTWARE\ADDS-PIM\Worker";

    public static WorkerHostSettings LoadFromLocalMachine()
    {
        using var key = Registry.LocalMachine.OpenSubKey(RegistryPath, writable: false)
            ?? throw new InvalidOperationException($"Configuration registry key HKLM\\{RegistryPath} does not exist.");

        return Create(
            ReadRequiredString(key, "BindAddress"),
            ReadRequiredInt32(key, "Port"),
            ReadRequiredString(key, "ServerCertificateThumbprint"),
            ReadRequiredString(key, "WorkerClientCertificateThumbprint"),
            ReadThumbprints(key),
            ReadRequiredString(key, "SqlConnectionString"),
            ReadRequiredString(key, "DomainController"),
            ReadRequiredInt32(key, "CommandMaxAgeSeconds"));
    }

    internal static WorkerHostSettings Create(
        string bindAddress,
        int port,
        string serverCertificateThumbprint,
        string workerClientCertificateThumbprint,
        IEnumerable<string> allowedClientThumbprints,
        string sqlConnectionString,
        string domainController,
        int commandMaxAgeSeconds)
    {
        if (!IPAddress.TryParse(bindAddress, out var address))
        {
            throw new InvalidOperationException("Worker BindAddress must be a literal IP address.");
        }

        if (IPAddress.Any.Equals(address) || IPAddress.IPv6Any.Equals(address) || IPAddress.Loopback.Equals(address) || IPAddress.IPv6Loopback.Equals(address))
        {
            throw new InvalidOperationException("Worker BindAddress must be a specific non-loopback interface address.");
        }

        if (port is < 1 or > 65535)
        {
            throw new InvalidOperationException("Worker Port must be between 1 and 65535.");
        }

        if (commandMaxAgeSeconds is < 30 or > 600)
        {
            throw new InvalidOperationException("Worker CommandMaxAgeSeconds must be between 30 and 600.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sqlConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(domainController);

        var allowed = allowedClientThumbprints
            .Select(NormalizeThumbprint)
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0)
        {
            throw new InvalidOperationException("At least one allowed API client certificate thumbprint is required.");
        }

        return new WorkerHostSettings(
            address,
            port,
            NormalizeThumbprint(serverCertificateThumbprint),
            NormalizeThumbprint(workerClientCertificateThumbprint),
            allowed,
            sqlConnectionString,
            domainController.Trim(),
            commandMaxAgeSeconds);
    }

    public static string NormalizeThumbprint(string? thumbprint)
        => string.Concat((thumbprint ?? string.Empty).Where(char.IsAsciiHexDigit)).ToUpperInvariant();

    private static string ReadRequiredString(RegistryKey key, string name)
        => key.GetValue(name) as string is { Length: > 0 } value
            ? value.Trim()
            : throw new InvalidOperationException($"Registry value {name} is required.");

    private static int ReadRequiredInt32(RegistryKey key, string name)
        => key.GetValue(name) switch
        {
            int value => value,
            string value when int.TryParse(value, out var parsed) => parsed,
            _ => throw new InvalidOperationException($"Registry value {name} must be a 32-bit integer.")
        };

    private static IEnumerable<string> ReadThumbprints(RegistryKey key)
        => key.GetValue("AllowedApiClientCertificateThumbprints") switch
        {
            string[] values => values,
            string value => value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            _ => throw new InvalidOperationException("Registry value AllowedApiClientCertificateThumbprints is required.")
        };
}
