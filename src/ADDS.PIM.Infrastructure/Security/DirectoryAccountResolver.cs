using System.DirectoryServices.Protocols;
using ADDS.PIM.Application.Administration;
using ADDS.PIM.Application.Authorization;
using Microsoft.Extensions.Options;

namespace ADDS.PIM.Infrastructure.Security;

internal sealed class DirectoryAccountResolver(IOptions<ApplicationAccessOptions> options, DirectoryScopeConfiguration directoryScope) : IDirectoryAccountResolver
{
    public Task<IReadOnlyList<ResolvedDirectoryAccount>> SearchPimUsersAsync(string searchTerm, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(searchTerm)) return Task.FromResult<IReadOnlyList<ResolvedDirectoryAccount>>([]);
        return SearchAsync(searchTerm.Trim(), null, cancellationToken);
    }

    public async Task<ResolvedDirectoryAccount?> ResolvePimUserAsync(Guid objectGuid, CancellationToken cancellationToken)
    {
        if (objectGuid == Guid.Empty) return null;
        var results = await SearchAsync(null, objectGuid, cancellationToken);
        return results.Count == 1 ? results[0] : null;
    }

    public Task<IReadOnlyList<ResolvedDirectoryAccount>> SearchAccountsAsync(string searchTerm, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(searchTerm)) return Task.FromResult<IReadOnlyList<ResolvedDirectoryAccount>>([]);
        return SearchAsync(searchTerm.Trim(), null, false, cancellationToken);
    }

    public async Task<ResolvedDirectoryAccount?> ResolveAccountAsync(Guid objectGuid, CancellationToken cancellationToken)
    {
        if (objectGuid == Guid.Empty) return null;
        var results = await SearchAsync(null, objectGuid, false, cancellationToken);
        return results.Count == 1 ? results[0] : null;
    }

    private Task<IReadOnlyList<ResolvedDirectoryAccount>> SearchAsync(string? searchTerm, Guid? objectGuid, CancellationToken cancellationToken)
        => SearchAsync(searchTerm, objectGuid, true, cancellationToken);

    private Task<IReadOnlyList<ResolvedDirectoryAccount>> SearchAsync(string? searchTerm, Guid? objectGuid, bool requirePimUserMembership, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var configuration = options.Value; configuration.Validate();
            using var connection = new LdapConnection(new LdapDirectoryIdentifier(configuration.DomainController!, 389)) { AuthType = AuthType.Negotiate };
            connection.SessionOptions.ProtocolVersion = 3; connection.SessionOptions.Signing = true; connection.SessionOptions.Sealing = true; connection.Bind();
            var root = (SearchResponse)connection.SendRequest(new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext"));
            var namingContext = root.Entries.Count == 1 && root.Entries[0].Attributes.Contains("defaultNamingContext") ? root.Entries[0].Attributes["defaultNamingContext"][0]?.ToString() : null;
            if (string.IsNullOrWhiteSpace(namingContext)) return Task.FromResult<IReadOnlyList<ResolvedDirectoryAccount>>([]);
            var userGroupDn = requirePimUserMembership ? ResolveGroupDn(connection, namingContext, configuration.UsersGroupObjectGuid) : null;
            if (requirePimUserMembership && userGroupDn is null) return Task.FromResult<IReadOnlyList<ResolvedDirectoryAccount>>([]);
            var identity = objectGuid is Guid guid ? "(objectGUID=" + ToOctetString(guid) + ")" : "(|(sAMAccountName=*" + Escape(searchTerm!) + "*)(displayName=*" + Escape(searchTerm!) + "*))";
            var membership = requirePimUserMembership ? "(memberOf:1.2.840.113556.1.4.1941:=" + Escape(userGroupDn!) + ")" : string.Empty;
            var filter = "(&" + identity + "(objectCategory=person)(objectClass=user)(!(objectClass=computer))" + membership + ")";
            var request = new SearchRequest(namingContext, filter, SearchScope.Subtree, "objectGUID", "sAMAccountName", "distinguishedName", "displayName", "userPrincipalName", "mail", "objectSid", "userAccountControl") { SizeLimit = 25 };
            var response = (SearchResponse)connection.SendRequest(request);
            var results = response.Entries.Cast<SearchResultEntry>().Select(ToAccount).Where(account => account is not null).Cast<ResolvedDirectoryAccount>().OrderBy(account => account.DomainQualifiedName, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult<IReadOnlyList<ResolvedDirectoryAccount>>(results);
        }
        catch (DirectoryOperationException) { return Task.FromResult<IReadOnlyList<ResolvedDirectoryAccount>>([]); }
        catch (LdapException) { return Task.FromResult<IReadOnlyList<ResolvedDirectoryAccount>>([]); }
    }

    private ResolvedDirectoryAccount? ToAccount(SearchResultEntry entry)
    {
        string? Attribute(string name) => entry.Attributes.Contains(name) && entry.Attributes[name].Count == 1 ? entry.Attributes[name][0]?.ToString() : null;
        var guid = entry.Attributes.Contains("objectGUID") && entry.Attributes["objectGUID"].Count == 1 && entry.Attributes["objectGUID"][0] is byte[] bytes && bytes.Length == 16 ? new Guid(bytes) : Guid.Empty;
        var sam = Attribute("sAMAccountName"); var display = Attribute("displayName") ?? sam; var uac = Attribute("userAccountControl");
        if (guid == Guid.Empty || string.IsNullOrWhiteSpace(sam) || string.IsNullOrWhiteSpace(display) || string.IsNullOrWhiteSpace(entry.DistinguishedName)) return null;
        var enabled = !int.TryParse(uac, out var flags) || (flags & 2) == 0;
        return new ResolvedDirectoryAccount(guid, sam, entry.DistinguishedName, $"{directoryScope.DomainDnsName}\\{sam}", display, Attribute("userPrincipalName"), Attribute("mail"), Attribute("objectSid"), enabled);
    }

    private static string? ResolveGroupDn(LdapConnection connection, string namingContext, Guid objectGuid)
    {
        var response = (SearchResponse)connection.SendRequest(new SearchRequest(namingContext, "(objectGUID=" + ToOctetString(objectGuid) + ")", SearchScope.Subtree, "distinguishedName"));
        return response.Entries.Count == 1 ? response.Entries[0].DistinguishedName : null;
    }
    private static string ToOctetString(Guid guid) => string.Concat(guid.ToByteArray().Select(value => "\\" + value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
    private static string Escape(string value) => value.Replace("\\", "\\5c", StringComparison.Ordinal).Replace("*", "\\2a", StringComparison.Ordinal).Replace("(", "\\28", StringComparison.Ordinal).Replace(")", "\\29", StringComparison.Ordinal).Replace("\0", "\\00", StringComparison.Ordinal);
}
