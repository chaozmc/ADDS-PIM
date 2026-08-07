namespace ADDS.PIM.Application.Mfa;

public sealed record PendingTotpEnrollment(Guid TotpFactorId, Guid PersonId, byte[] EncryptedSecret, string ProtectionKeyId, DateTimeOffset EnrollmentExpiresUtc);

public sealed record ConfirmTotpEnrollmentCommand(Guid PersonId, Guid FactorId, string Code, Guid CorrelationId, string FrontendClientId, string? SourceIpAddress, string? ClientSourceIpAddress);

public interface ITotpEnrollmentConfirmationStore
{
    Task<PendingTotpEnrollment?> FindPendingAsync(Guid personId, Guid factorId, CancellationToken cancellationToken);
    Task<bool> ConfirmAsync(Guid personId, Guid factorId, DateTimeOffset confirmedUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken);
    Task RecordRejectionAsync(Guid personId, Guid factorId, string eventType, DateTimeOffset occurredUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken);
}

public sealed class ConfirmTotpEnrollmentUseCase(ITotpSecretProtector protector, ITotpEnrollmentConfirmationStore store, TimeProvider timeProvider)
{
    public async Task<bool> ExecuteAsync(ConfirmTotpEnrollmentCommand command, CancellationToken cancellationToken)
    {
        if (command.PersonId == Guid.Empty || command.FactorId == Guid.Empty) throw new ArgumentException("A person and factor ID are required.", nameof(command));
        var now = timeProvider.GetUtcNow();
        var factor = await store.FindPendingAsync(command.PersonId, command.FactorId, cancellationToken);
        if (factor is null)
        {
            await store.RecordRejectionAsync(command.PersonId, command.FactorId, "TotpEnrollmentConfirmationRejectedNotFound", now, command.CorrelationId, command.FrontendClientId, command.SourceIpAddress, command.ClientSourceIpAddress, cancellationToken);
            return false;
        }

        if (now >= factor.EnrollmentExpiresUtc)
        {
            await store.RecordRejectionAsync(command.PersonId, command.FactorId, "TotpEnrollmentConfirmationExpired", now, command.CorrelationId, command.FrontendClientId, command.SourceIpAddress, command.ClientSourceIpAddress, cancellationToken);
            return false;
        }

        var secret = protector.Unprotect(factor.EncryptedSecret, factor.ProtectionKeyId);
        if (!Totp.TryValidate(secret, command.Code, now, out _))
        {
            await store.RecordRejectionAsync(command.PersonId, command.FactorId, "TotpEnrollmentConfirmationRejectedInvalidCode", now, command.CorrelationId, command.FrontendClientId, command.SourceIpAddress, command.ClientSourceIpAddress, cancellationToken);
            return false;
        }

        return await store.ConfirmAsync(command.PersonId, command.FactorId, now, command.CorrelationId, command.FrontendClientId, command.SourceIpAddress, command.ClientSourceIpAddress, cancellationToken);
    }
}
