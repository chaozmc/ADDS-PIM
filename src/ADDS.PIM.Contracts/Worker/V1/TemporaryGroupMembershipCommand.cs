using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ADDS.PIM.Contracts.Worker.V1;

/// <summary>
/// Versioned, typed command for the only membership-changing worker operation.
/// All directory identities are immutable AD object GUIDs, not names or DNs.
/// </summary>
public sealed record TemporaryGroupMembershipCommand(
    string Version,
    Guid CommandId,
    Guid RequestId,
    Guid CorrelationId,
    DateTimeOffset IssuedUtc,
    string Nonce,
    Guid DirectoryScopeId,
    Guid TargetAccountObjectGuid,
    Guid TargetGroupObjectGuid,
    long RequestedTtlSeconds,
    string CommandHash)
{
    public const string CurrentVersion = "worker-command-v1";
}

public static class TemporaryGroupMembershipCommandCanonicalizer
{
    public static string ComputeHash(TemporaryGroupMembershipCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(CreateCanonicalRepresentation(command))));
    }

    public static bool HasValidHash(TemporaryGroupMembershipCommand command)
    {
        if (command is null || string.IsNullOrWhiteSpace(command.CommandHash)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(ComputeHash(command)),
            Encoding.ASCII.GetBytes(command.CommandHash));
    }

    public static string CreateCanonicalRepresentation(TemporaryGroupMembershipCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command);
        return string.Join('\n',
        [
            string.Create(CultureInfo.InvariantCulture, $"version={command.Version}"),
            string.Create(CultureInfo.InvariantCulture, $"commandId={command.CommandId:D}"),
            string.Create(CultureInfo.InvariantCulture, $"requestId={command.RequestId:D}"),
            string.Create(CultureInfo.InvariantCulture, $"correlationId={command.CorrelationId:D}"),
            string.Create(CultureInfo.InvariantCulture, $"issuedUtc={command.IssuedUtc.UtcDateTime:O}"),
            string.Create(CultureInfo.InvariantCulture, $"nonce={command.Nonce}"),
            string.Create(CultureInfo.InvariantCulture, $"directoryScopeId={command.DirectoryScopeId:D}"),
            string.Create(CultureInfo.InvariantCulture, $"targetAccountObjectGuid={command.TargetAccountObjectGuid:D}"),
            string.Create(CultureInfo.InvariantCulture, $"targetGroupObjectGuid={command.TargetGroupObjectGuid:D}"),
            string.Create(CultureInfo.InvariantCulture, $"requestedTtlSeconds={command.RequestedTtlSeconds}")
        ]);
    }

    private static void Validate(TemporaryGroupMembershipCommand command)
    {
        if (!StringComparer.Ordinal.Equals(command.Version, TemporaryGroupMembershipCommand.CurrentVersion)
            || command.CommandId == Guid.Empty || command.RequestId == Guid.Empty || command.CorrelationId == Guid.Empty
            || command.DirectoryScopeId == Guid.Empty || command.TargetAccountObjectGuid == Guid.Empty || command.TargetGroupObjectGuid == Guid.Empty
            || command.RequestedTtlSeconds <= 0 || !IsBase64Url(command.Nonce))
        {
            throw new ArgumentException("The worker command is invalid.", nameof(command));
        }
    }

    private static bool IsBase64Url(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string Base64UrlEncode(byte[] value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
