using ADDS.PIM.Application.Mfa;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfTotpFactorEnrollmentStore(PimDbContext dbContext) : ITotpFactorEnrollmentStore
{
    public Task<bool> HasActiveFactorAsync(Guid personId, CancellationToken cancellationToken)
        => dbContext.TotpFactors.AsNoTracking().AnyAsync(x => x.PersonId == personId && x.IsActive && x.RevokedUtc == null, cancellationToken);

    public async Task CreateAsync(NewTotpFactor factor, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken)
    {
        dbContext.TotpFactors.Add(new TotpFactorEntity { TotpFactorId = factor.TotpFactorId, PersonId = factor.PersonId, EncryptedSecret = factor.EncryptedSecret, ProtectionKeyId = factor.ProtectionKeyId, EnrolledUtc = factor.EnrolledUtc, EnrollmentExpiresUtc = factor.EnrollmentExpiresUtc, IsActive = false });
        dbContext.AuditEvents.Add(new Entities.AuditEventEntity
        {
            EventId = Guid.NewGuid(),
            EventType = "TotpFactorEnrollmentStarted",
            OccurredUtc = factor.EnrolledUtc,
            PersonId = factor.PersonId,
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
}
