using System.Security.Cryptography;
using System.Text;

namespace ADDS.PIM.Application.Mfa;

/// <summary>
/// Computes the durable "mfa-transaction-v1" tamper/identity marker for a membership-request tuple
///. This hash is an audit-correlation artifact, not a cryptographic binding by itself &mdash;
/// the one-use MFA transaction row and its consumption guard are the actual binding.
/// </summary>
public static class MfaTransactionCanonicalizer
{
    public const string CurrentVersion = "mfa-transaction-v1";

    public static string CreateCanonicalRepresentation(
        Guid requestId,
        Guid personId,
        Guid actorAccountId,
        Guid targetAccountId,
        Guid targetGroupId,
        long requestedTtlSeconds)
        => string.Join('\n',
        [
            $"version={CurrentVersion}",
            $"requestId={requestId:D}",
            $"personId={personId:D}",
            $"actorAccountId={actorAccountId:D}",
            $"targetAccountId={targetAccountId:D}",
            $"targetGroupId={targetGroupId:D}",
            $"requestedTtlSeconds={requestedTtlSeconds}"
        ]);

    public static string ComputeHash(
        Guid requestId,
        Guid personId,
        Guid actorAccountId,
        Guid targetAccountId,
        Guid targetGroupId,
        long requestedTtlSeconds)
    {
        var canonical = CreateCanonicalRepresentation(requestId, personId, actorAccountId, targetAccountId, targetGroupId, requestedTtlSeconds);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
