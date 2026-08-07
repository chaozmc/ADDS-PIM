using System.DirectoryServices.Protocols;
using ADDS.PIM.Application.Security;
using Microsoft.Extensions.Options;

namespace ADDS.PIM.Infrastructure.Security;

public sealed class ApplicationAccessOptions
{
    public const string SectionName = "ApplicationAccess";
    public string? DomainController { get; init; }
    public Guid UsersGroupObjectGuid { get; init; }
    public Guid AdministratorsGroupObjectGuid { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DomainController) || UsersGroupObjectGuid == Guid.Empty || AdministratorsGroupObjectGuid == Guid.Empty)
            throw new InvalidOperationException("ApplicationAccess configuration is incomplete.");
    }
}

internal sealed class DirectoryGroupAccessAuthorizer(IOptions<ApplicationAccessOptions> options) : IApplicationAccessAuthorizer
{
    public Task<bool> IsAuthorizedAsync(Guid actorObjectGuid, ApplicationAccessLevel requiredAccess, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configuration = options.Value; configuration.Validate();
        try
        {
            using var connection = new LdapConnection(new LdapDirectoryIdentifier(configuration.DomainController!, 389)) { AuthType = AuthType.Negotiate };
            connection.SessionOptions.ProtocolVersion = 3; connection.SessionOptions.Signing = true; connection.SessionOptions.Sealing = true; connection.Bind();
            var root = (SearchResponse)connection.SendRequest(new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext"));
            if (root.Entries.Count != 1 || !root.Entries[0].Attributes.Contains("defaultNamingContext")) return Task.FromResult(false);
            var namingContext = root.Entries[0].Attributes["defaultNamingContext"][0].ToString();
            if (string.IsNullOrWhiteSpace(namingContext)) return Task.FromResult(false);
            if (!IsTransitivelyMember(connection, namingContext, actorObjectGuid, configuration.UsersGroupObjectGuid)) return Task.FromResult(false);
            return Task.FromResult(requiredAccess != ApplicationAccessLevel.Administrator || IsTransitivelyMember(connection, namingContext, actorObjectGuid, configuration.AdministratorsGroupObjectGuid));
        }
        catch (DirectoryOperationException) { return Task.FromResult(false); }
        catch (LdapException) { return Task.FromResult(false); }
    }

    public Task<bool> IsAuthorizedAsync(string actorSid, ApplicationAccessLevel requiredAccess, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(actorSid)) return Task.FromResult(false);

        try
        {
            var configuration = options.Value; configuration.Validate();
            using var connection = new LdapConnection(new LdapDirectoryIdentifier(configuration.DomainController!, 389)) { AuthType = AuthType.Negotiate };
            connection.SessionOptions.ProtocolVersion = 3; connection.SessionOptions.Signing = true; connection.SessionOptions.Sealing = true; connection.Bind();
            var root = (SearchResponse)connection.SendRequest(new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext"));
            if (root.Entries.Count != 1 || !root.Entries[0].Attributes.Contains("defaultNamingContext")) return Task.FromResult(false);
            var namingContext = root.Entries[0].Attributes["defaultNamingContext"][0].ToString();
            if (string.IsNullOrWhiteSpace(namingContext)) return Task.FromResult(false);
            var actorDn = ResolveDnBySid(connection, namingContext, actorSid);
            if (actorDn is null || !IsTransitivelyMember(connection, namingContext, actorDn, configuration.UsersGroupObjectGuid)) return Task.FromResult(false);
            return Task.FromResult(requiredAccess != ApplicationAccessLevel.Administrator || IsTransitivelyMember(connection, namingContext, actorDn, configuration.AdministratorsGroupObjectGuid));
        }
        catch (ArgumentException) { return Task.FromResult(false); }
        catch (DirectoryOperationException) { return Task.FromResult(false); }
        catch (LdapException) { return Task.FromResult(false); }
    }

    public Task<Guid?> ResolveActorObjectGuidAsync(string actorSid, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(actorSid)) return Task.FromResult<Guid?>(null);
        try
        {
            var configuration = options.Value; configuration.Validate();
            using var connection = new LdapConnection(new LdapDirectoryIdentifier(configuration.DomainController!, 389)) { AuthType = AuthType.Negotiate };
            connection.SessionOptions.ProtocolVersion = 3; connection.SessionOptions.Signing = true; connection.SessionOptions.Sealing = true; connection.Bind();
            var root = (SearchResponse)connection.SendRequest(new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext"));
            var namingContext = root.Entries.Count == 1 && root.Entries[0].Attributes.Contains("defaultNamingContext") ? root.Entries[0].Attributes["defaultNamingContext"][0]?.ToString() : null;
            if (string.IsNullOrWhiteSpace(namingContext)) return Task.FromResult<Guid?>(null);
            var sid = DirectorySidEncoder.ToBinarySid(actorSid);
            var filter = "(objectSid=" + string.Concat(sid.Select(value => "\\" + value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture))) + ")";
            var result = (SearchResponse)connection.SendRequest(new SearchRequest(namingContext, filter, SearchScope.Subtree, "objectGUID"));
            if (result.Entries.Count != 1 || !result.Entries[0].Attributes.Contains("objectGUID") || result.Entries[0].Attributes["objectGUID"].Count != 1 || result.Entries[0].Attributes["objectGUID"][0] is not byte[] bytes || bytes.Length != 16) return Task.FromResult<Guid?>(null);
            return Task.FromResult<Guid?>(new Guid(bytes));
        }
        catch (ArgumentException) { return Task.FromResult<Guid?>(null); }
        catch (DirectoryOperationException) { return Task.FromResult<Guid?>(null); }
        catch (LdapException) { return Task.FromResult<Guid?>(null); }
    }

    private static string? ResolveDn(LdapConnection connection, string namingContext, Guid objectGuid)
    {
        var filter = "(objectGUID=" + string.Concat(objectGuid.ToByteArray().Select(value => "\\" + value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture))) + ")";
        var result = (SearchResponse)connection.SendRequest(new SearchRequest(namingContext, filter, SearchScope.Subtree, "distinguishedName"));
        return result.Entries.Count == 1 ? result.Entries[0].DistinguishedName : null;
    }

    private static string? ResolveDnBySid(LdapConnection connection, string namingContext, string actorSid)
    {
        var bytes = DirectorySidEncoder.ToBinarySid(actorSid);
        var filter = "(objectSid=" + string.Concat(bytes.Select(value => "\\" + value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture))) + ")";
        var result = (SearchResponse)connection.SendRequest(new SearchRequest(namingContext, filter, SearchScope.Subtree, "distinguishedName"));
        return result.Entries.Count == 1 ? result.Entries[0].DistinguishedName : null;
    }

    private static bool IsTransitivelyMember(LdapConnection connection, string namingContext, Guid actorObjectGuid, Guid groupObjectGuid)
    {
        var groupDn = ResolveDn(connection, namingContext, groupObjectGuid);
        if (groupDn is null) return false;
        var actorFilter = "(objectGUID=" + string.Concat(actorObjectGuid.ToByteArray().Select(value => "\\" + value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture))) + ")";
        return IsTransitivelyMember(connection, namingContext, actorFilter, groupDn);
    }

    private static bool IsTransitivelyMember(LdapConnection connection, string namingContext, string actorDn, Guid groupObjectGuid)
    {
        var groupDn = ResolveDn(connection, namingContext, groupObjectGuid);
        return groupDn is not null && IsTransitivelyMember(connection, namingContext, "(distinguishedName=" + EscapeFilterValue(actorDn) + ")", groupDn);
    }

    private static bool IsTransitivelyMember(LdapConnection connection, string namingContext, string actorFilter, string groupDn)
    {
        var filter = "(&" + actorFilter + "(memberOf:1.2.840.113556.1.4.1941:=" + EscapeFilterValue(groupDn) + "))";
        var result = (SearchResponse)connection.SendRequest(new SearchRequest(namingContext, filter, SearchScope.Subtree, "objectGUID"));
        return result.Entries.Count == 1;
    }

    private static string EscapeFilterValue(string value)
        => value.Replace("\\", "\\5c", StringComparison.Ordinal).Replace("*", "\\2a", StringComparison.Ordinal).Replace("(", "\\28", StringComparison.Ordinal).Replace(")", "\\29", StringComparison.Ordinal).Replace("\0", "\\00", StringComparison.Ordinal);
}
