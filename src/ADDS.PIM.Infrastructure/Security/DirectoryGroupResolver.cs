using System.DirectoryServices.Protocols;
using ADDS.PIM.Application.Administration;
using ADDS.PIM.Application.Authorization;
using Microsoft.Extensions.Options;

namespace ADDS.PIM.Infrastructure.Security;

internal sealed class DirectoryGroupResolver(IOptions<ApplicationAccessOptions> options, DirectoryScopeConfiguration directoryScope) : IDirectoryGroupResolver
{
    public Task<ResolvedDirectoryGroup?> ResolveAsync(Guid objectGuid, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (objectGuid == Guid.Empty) return Task.FromResult<ResolvedDirectoryGroup?>(null);
        try
        {
            var configuration = options.Value; configuration.Validate();
            using var connection = new LdapConnection(new LdapDirectoryIdentifier(configuration.DomainController!, 389)) { AuthType = AuthType.Negotiate };
            connection.SessionOptions.ProtocolVersion = 3; connection.SessionOptions.Signing = true; connection.SessionOptions.Sealing = true; connection.Bind();
            var root = (SearchResponse)connection.SendRequest(new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext"));
            var namingContext = root.Entries.Count == 1 && root.Entries[0].Attributes.Contains("defaultNamingContext") ? root.Entries[0].Attributes["defaultNamingContext"][0]?.ToString() : null;
            if (string.IsNullOrWhiteSpace(namingContext)) return Task.FromResult<ResolvedDirectoryGroup?>(null);
            var filter = "(&(objectGUID=" + string.Concat(objectGuid.ToByteArray().Select(value => "\\" + value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture))) + ")(objectCategory=group))";
            var response = (SearchResponse)connection.SendRequest(new SearchRequest(namingContext, filter, SearchScope.Subtree, "objectGUID", "sAMAccountName", "distinguishedName", "displayName", "objectSid"));
            if (response.Entries.Count != 1) return Task.FromResult<ResolvedDirectoryGroup?>(null);
            return Task.FromResult(ToGroup(response.Entries[0], objectGuid));
        }
        catch (DirectoryOperationException) { return Task.FromResult<ResolvedDirectoryGroup?>(null); }
        catch (LdapException) { return Task.FromResult<ResolvedDirectoryGroup?>(null); }
    }

    public Task<IReadOnlyList<ResolvedDirectoryGroup>> SearchAsync(string searchTerm, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(searchTerm)) return Task.FromResult<IReadOnlyList<ResolvedDirectoryGroup>>([]);
        try
        {
            var configuration = options.Value; configuration.Validate();
            using var connection = new LdapConnection(new LdapDirectoryIdentifier(configuration.DomainController!, 389)) { AuthType = AuthType.Negotiate };
            connection.SessionOptions.ProtocolVersion = 3; connection.SessionOptions.Signing = true; connection.SessionOptions.Sealing = true; connection.Bind();
            var root = (SearchResponse)connection.SendRequest(new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext"));
            var namingContext = root.Entries.Count == 1 && root.Entries[0].Attributes.Contains("defaultNamingContext") ? root.Entries[0].Attributes["defaultNamingContext"][0]?.ToString() : null;
            if (string.IsNullOrWhiteSpace(namingContext)) return Task.FromResult<IReadOnlyList<ResolvedDirectoryGroup>>([]);
            var term = EscapeFilterValue(searchTerm.Trim());
            var filter = $"(&(objectCategory=group)(|(sAMAccountName=*{term}*)(displayName=*{term}*)))";
            var request = new SearchRequest(namingContext, filter, SearchScope.Subtree, "objectGUID", "sAMAccountName", "distinguishedName", "displayName", "objectSid") { SizeLimit = 25 };
            var response = (SearchResponse)connection.SendRequest(request);
            var groups = response.Entries.Cast<SearchResultEntry>().Select(entry => ToGroup(entry, ReadGuid(entry, "objectGUID"))).Where(group => group is not null).Cast<ResolvedDirectoryGroup>().OrderBy(group => group.DomainQualifiedName, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult<IReadOnlyList<ResolvedDirectoryGroup>>(groups);
        }
        catch (DirectoryOperationException) { return Task.FromResult<IReadOnlyList<ResolvedDirectoryGroup>>([]); }
        catch (LdapException) { return Task.FromResult<IReadOnlyList<ResolvedDirectoryGroup>>([]); }
    }

    private ResolvedDirectoryGroup? ToGroup(SearchResultEntry entry, Guid objectGuid)
    {
        string? Attribute(string name) => entry.Attributes.Contains(name) && entry.Attributes[name].Count == 1 ? entry.Attributes[name][0]?.ToString() : null;
        var sam = Attribute("sAMAccountName"); var distinguishedName = entry.DistinguishedName; var displayName = Attribute("displayName") ?? sam;
        return objectGuid == Guid.Empty || string.IsNullOrWhiteSpace(sam) || string.IsNullOrWhiteSpace(distinguishedName) || string.IsNullOrWhiteSpace(displayName) ? null : new(objectGuid, sam, distinguishedName, $"{directoryScope.DomainDnsName}\\{sam}", displayName, Attribute("objectSid"));
    }

    private static Guid ReadGuid(SearchResultEntry entry, string attribute)
        => entry.Attributes.Contains(attribute) && entry.Attributes[attribute].Count == 1 && entry.Attributes[attribute][0] is byte[] value && value.Length == 16 ? new Guid(value) : Guid.Empty;

    private static string EscapeFilterValue(string value)
        => value.Replace("\\", "\\5c", StringComparison.Ordinal).Replace("*", "\\2a", StringComparison.Ordinal).Replace("(", "\\28", StringComparison.Ordinal).Replace(")", "\\29", StringComparison.Ordinal).Replace("\0", "\\00", StringComparison.Ordinal);
}
