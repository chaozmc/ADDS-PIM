using System.Security.Cryptography;

namespace ADDS.PIM.Application.Mfa;

public sealed record CreateTotpEnrollmentCommand(Guid PersonId, Guid CorrelationId, string FrontendClientId, string? SourceIpAddress, string? ClientSourceIpAddress);

/// <summary>The raw secret is returned only to the current enrollment flow and must never be logged or persisted.</summary>
public sealed record TotpEnrollment(Guid TotpFactorId, byte[] Secret, string ProtectionKeyId, DateTimeOffset EnrollmentExpiresUtc);

public sealed record NewTotpFactor(Guid TotpFactorId, Guid PersonId, byte[] EncryptedSecret, string ProtectionKeyId, DateTimeOffset EnrolledUtc, DateTimeOffset EnrollmentExpiresUtc);

public interface ITotpFactorEnrollmentStore
{
    Task<bool> HasActiveFactorAsync(Guid personId, CancellationToken cancellationToken);

    Task CreateAsync(NewTotpFactor factor, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken);
}

/// <summary>
/// Starts a TOTP enrollment. Self-service re-enrollment is not permitted while an active
/// factor already exists - resetting an existing factor requires a separately authorized, audited
/// administrative workflow (not implemented in the MVP). Returns null when an active factor already exists.
/// </summary>
public sealed class CreateTotpEnrollmentUseCase(ITotpSecretProtector protector, ITotpFactorEnrollmentStore store, TimeProvider timeProvider)
{
    public async Task<TotpEnrollment?> ExecuteAsync(CreateTotpEnrollmentCommand command, CancellationToken cancellationToken)
    {
        if (command.PersonId == Guid.Empty) throw new ArgumentException("A person ID is required.", nameof(command));
        if (await store.HasActiveFactorAsync(command.PersonId, cancellationToken))
        {
            return null;
        }

        var secret = RandomNumberGenerator.GetBytes(20);
        var factorId = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();
        var expiresUtc = now.AddMinutes(5);
        await store.CreateAsync(new NewTotpFactor(factorId, command.PersonId, protector.Protect(secret), protector.KeyId, now, expiresUtc), command.CorrelationId, command.FrontendClientId, command.SourceIpAddress, command.ClientSourceIpAddress, cancellationToken);
        return new TotpEnrollment(factorId, secret, protector.KeyId, expiresUtc);
    }
}
