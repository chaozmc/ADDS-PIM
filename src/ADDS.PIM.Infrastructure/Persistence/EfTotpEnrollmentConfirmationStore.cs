using ADDS.PIM.Application.Mfa;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfTotpEnrollmentConfirmationStore(PimDbContext dbContext, ILogger<EfTotpEnrollmentConfirmationStore> logger) : ITotpEnrollmentConfirmationStore
{
    public Task<PendingTotpEnrollment?> FindPendingAsync(Guid personId, Guid factorId, CancellationToken cancellationToken)
        => dbContext.TotpFactors.AsNoTracking().Where(x => x.PersonId == personId && x.TotpFactorId == factorId && !x.IsActive && x.ConfirmedUtc == null && x.RevokedUtc == null)
            .Select(x => new PendingTotpEnrollment(x.TotpFactorId, x.PersonId, x.EncryptedSecret, x.ProtectionKeyId, x.EnrollmentExpiresUtc)).SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> ConfirmAsync(Guid personId, Guid factorId, DateTimeOffset confirmedUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken)
    {
        bool updated;
        try
        {
            updated = await dbContext.TotpFactors.Where(x => x.PersonId == personId && x.TotpFactorId == factorId && !x.IsActive && x.ConfirmedUtc == null && x.RevokedUtc == null && x.EnrollmentExpiresUtc > confirmedUtc)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsActive, true).SetProperty(x => x.ConfirmedUtc, confirmedUtc), cancellationToken) == 1;
        }
        catch (DbUpdateException ex)
        {
            // Defense-in-depth backstop for the (PersonId) unique filtered index on IsActive=1: the
            // use-case-level HasActiveFactorAsync check at enrollment start closes this window in the
            // normal case, but a genuine race between two concurrent enrollments must still fail cleanly
            // here rather than surface as an unhandled 500.
            logger.LogWarning(ex, "ConfirmAsync hit the (PersonId) unique-active-factor index for PersonId {PersonId}, TotpFactorId {TotpFactorId} - likely a concurrent enrollment race.", personId, factorId);
            updated = false;
        }

        if (updated)
        {
            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                EventId = Guid.NewGuid(),
                EventType = "TotpFactorConfirmed",
                OccurredUtc = confirmedUtc,
                PersonId = personId,
                FrontendClientId = frontendClientId,
                SourceIpAddress = sourceIpAddress,
                ClientSourceIpAddress = clientSourceIpAddress,
                SourceComponent = "Api",
                CorrelationId = correlationId,
                Result = "Succeeded",
                AuthenticationMethod = "Windows",
                PolicyRequirementsSummary = "TotpFactorEnrollment"
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return updated;
    }

    public async Task RecordRejectionAsync(Guid personId, Guid factorId, string eventType, DateTimeOffset occurredUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken)
    {
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            EventId = Guid.NewGuid(),
            EventType = eventType,
            OccurredUtc = occurredUtc,
            PersonId = personId,
            FrontendClientId = frontendClientId,
            SourceIpAddress = sourceIpAddress,
            ClientSourceIpAddress = clientSourceIpAddress,
            SourceComponent = "Api",
            CorrelationId = correlationId,
            Result = "Failed",
            FailureCategory = "Mfa",
            AuthenticationMethod = "Windows",
            PolicyRequirementsSummary = "TotpFactorEnrollment"
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
