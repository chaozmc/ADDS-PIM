using System.DirectoryServices.Protocols;
using ADDS.PIM.Application.Administration;
using Microsoft.Extensions.Options;

namespace ADDS.PIM.Infrastructure.Security;

internal sealed class DirectoryReconciliationResolver(IOptions<ApplicationAccessOptions> options) : IDirectoryReconciliationResolver
{
    public Task<DirectoryObjectLookupResult> ResolveAccountAsync(Guid objectGuid, CancellationToken cancellationToken)
        => ResolveAsync(objectGuid, "(&(objectCategory=person)(objectClass=user)(!(objectClass=computer)))", true, cancellationToken);

    public Task<DirectoryObjectLookupResult> ResolveGroupAsync(Guid objectGuid, CancellationToken cancellationToken)
        => ResolveAsync(objectGuid, "(objectCategory=group)", false, cancellationToken);

    private Task<DirectoryObjectLookupResult> ResolveAsync(Guid objectGuid, string objectFilter, bool readEnabledState, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (objectGuid == Guid.Empty) throw new ArgumentException("Directory object GUID is required.", nameof(objectGuid));

        var configuration = options.Value; configuration.Validate();
        using var connection = new LdapConnection(new LdapDirectoryIdentifier(configuration.DomainController!, 389)) { AuthType = AuthType.Negotiate };
        connection.SessionOptions.ProtocolVersion = 3; connection.SessionOptions.Signing = true; connection.SessionOptions.Sealing = true; connection.Bind();
        var root = (SearchResponse)connection.SendRequest(new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext"));
        var namingContext = root.Entries.Count == 1 && root.Entries[0].Attributes.Contains("defaultNamingContext") ? root.Entries[0].Attributes["defaultNamingContext"][0]?.ToString() : null;
        if (string.IsNullOrWhiteSpace(namingContext)) throw new DirectoryOperationException("Directory default naming context is unavailable.");

        var attributes = readEnabledState ? new[] { "objectGUID", "userAccountControl" } : new[] { "objectGUID" };
        var filter = "(&" + objectFilter + "(objectGUID=" + ToOctetString(objectGuid) + "))";
        var response = (SearchResponse)connection.SendRequest(new SearchRequest(namingContext, filter, SearchScope.Subtree, attributes));
        if (response.Entries.Count == 0) return Task.FromResult(new DirectoryObjectLookupResult(DirectoryObjectLookupStatus.NotFound, false, false));
        if (response.Entries.Count != 1) return Task.FromResult(new DirectoryObjectLookupResult(DirectoryObjectLookupStatus.Ambiguous, false, false));

        var isEnabled = true;
        if (readEnabledState)
        {
            var uac = response.Entries[0].Attributes.Contains("userAccountControl") && response.Entries[0].Attributes["userAccountControl"].Count == 1
                ? response.Entries[0].Attributes["userAccountControl"][0]?.ToString() : null;
            isEnabled = !int.TryParse(uac, out var flags) || (flags & 2) == 0;
        }
        return Task.FromResult(new DirectoryObjectLookupResult(DirectoryObjectLookupStatus.Found, isEnabled, true));
    }

    private static string ToOctetString(Guid guid)
        => string.Concat(guid.ToByteArray().Select(value => "\\" + value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
}
