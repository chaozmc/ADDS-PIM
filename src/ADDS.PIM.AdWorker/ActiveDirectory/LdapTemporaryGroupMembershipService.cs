using System.DirectoryServices.Protocols;
using ADDS.PIM.Application.Worker;
using Microsoft.Extensions.Options;

namespace ADDS.PIM.AdWorker.ActiveDirectory;

public sealed class LdapTemporaryGroupMembershipService(IOptions<ActiveDirectoryOptions> options) : ITemporaryGroupMembershipService
{
    private const string LinkTtlControlOid = "1.2.840.113556.1.4.2309";

    public async Task<TemporaryGroupMembershipResult> AddAndVerifyAsync(
        TemporaryGroupMembershipOperation operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var server = options.Value.DomainController;
        if (operation.TargetAccountObjectGuid == Guid.Empty ||
            operation.TargetGroupObjectGuid == Guid.Empty ||
            operation.RequestedTtlSeconds <= 0 ||
            string.IsNullOrWhiteSpace(server))
        {
            throw new ArgumentException("The LDAP operation is incomplete.", nameof(operation));
        }

        try
        {
            using var connection = new LdapConnection(new LdapDirectoryIdentifier(server, 389))
            {
                AuthType = AuthType.Negotiate
            };
            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.Signing = true;
            connection.SessionOptions.Sealing = true;
            connection.Bind();

            var root = (SearchResponse)connection.SendRequest(new SearchRequest(
                null,
                "(objectClass=*)",
                SearchScope.Base,
                "defaultNamingContext",
                "supportedControl"));
            if (root.Entries.Count != 1)
            {
                throw new InvalidOperationException("The DC root DSE response was missing or ambiguous.");
            }

            var entry = root.Entries[0];
            if (!entry.Attributes.Contains("defaultNamingContext") ||
                !entry.Attributes.Contains("supportedControl") ||
                !entry.Attributes["supportedControl"].GetValues(typeof(string)).Cast<string>().Contains(LinkTtlControlOid, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("The DC does not support expiring LDAP links.");
            }

            var context = entry.Attributes["defaultNamingContext"][0].ToString()!;
            var userDn = ResolveDn(connection, context, operation.TargetAccountObjectGuid);
            var groupDn = ResolveDn(connection, context, operation.TargetGroupObjectGuid);
            if (ReadTtl(connection, groupDn, userDn) is not null)
            {
                return new TemporaryGroupMembershipResult(
                    TemporaryGroupMembershipResultKind.ExistingMembership,
                    server,
                    null,
                    "ExistingMembership");
            }

            var ttlDn = $"<TTL={operation.RequestedTtlSeconds},{userDn}>";
            connection.SendRequest(new ModifyRequest(groupDn, DirectoryAttributeOperation.Add, "member", ttlDn));

            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                var ttl = ReadTtl(connection, groupDn, userDn);
                if (ttl is > 0 && ttl <= operation.RequestedTtlSeconds && ttl >= operation.RequestedTtlSeconds - 45)
                {
                    return new TemporaryGroupMembershipResult(
                        TemporaryGroupMembershipResultKind.Verified,
                        server,
                        ttl,
                        null);
                }
            }

            return new TemporaryGroupMembershipResult(
                TemporaryGroupMembershipResultKind.VerificationFailed,
                server,
                null,
                "VerificationFailed");
        }
        catch (DirectoryOperationException error)
        {
            return new TemporaryGroupMembershipResult(
                TemporaryGroupMembershipResultKind.ActiveDirectoryFailure,
                server,
                null,
                $"Ldap_{error.Response.ResultCode}");
        }
        catch (LdapException error)
        {
            return new TemporaryGroupMembershipResult(
                TemporaryGroupMembershipResultKind.ActiveDirectoryFailure,
                server,
                null,
                $"Ldap_{error.ErrorCode}");
        }
    }

    private static string ResolveDn(LdapConnection connection, string context, Guid guid)
    {
        var filter = "(objectGUID=" + string.Concat(guid.ToByteArray().Select(value =>
            "\\" + value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture))) + ")";
        var response = (SearchResponse)connection.SendRequest(new SearchRequest(context, filter, SearchScope.Subtree, "distinguishedName"));
        return response.Entries.Count == 1 ? response.Entries[0].DistinguishedName : throw new InvalidOperationException("Directory object resolution was missing or ambiguous.");
    }

    private static long? ReadTtl(LdapConnection connection, string groupDn, string userDn)
    {
        var request = new SearchRequest(groupDn, "(objectClass=*)", SearchScope.Base, "member");
        request.Controls.Add(new DirectoryControl(LinkTtlControlOid, null, true, true));
        var response = (SearchResponse)connection.SendRequest(request);
        if (response.Entries.Count != 1 || !response.Entries[0].Attributes.Contains("member"))
        {
            return null;
        }

        var values = response.Entries[0].Attributes["member"].GetValues(typeof(string)).Cast<string>();
        foreach (var value in values)
        {
            var marker = value.IndexOf(">,", StringComparison.Ordinal);
            if (value.StartsWith("<TTL=", StringComparison.Ordinal) &&
                marker > 5 &&
                value[(marker + 2)..].Equals(userDn, StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(value[5..marker], out var ttl))
            {
                return ttl;
            }

            if (value.Equals(userDn, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }
        }
        return null;
    }
}
