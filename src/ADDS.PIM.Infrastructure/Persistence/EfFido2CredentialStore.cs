using ADDS.PIM.Application.Mfa;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfFido2CredentialStore(PimDbContext dbContext) : IFido2CredentialStore
{
    public Task<int> CountActiveAsync(Guid personId, CancellationToken cancellationToken)
        => dbContext.Fido2Credentials.AsNoTracking().CountAsync(x => x.PersonId == personId && x.RevokedUtc == null, cancellationToken);

    public async Task<IReadOnlyList<ActiveFido2Credential>> ListActiveAsync(Guid personId, CancellationToken cancellationToken)
        => await dbContext.Fido2Credentials.AsNoTracking()
            .Where(x => x.PersonId == personId && x.RevokedUtc == null)
            .Select(x => new ActiveFido2Credential(x.Fido2CredentialId, x.PersonId, x.CredentialId, x.PublicKey, x.SignatureCounter, x.Label, x.CreatedUtc))
            .ToArrayAsync(cancellationToken);

    public async Task CreateAsync(NewStoredFido2Credential credential, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken)
    {
        dbContext.Fido2Credentials.Add(new Fido2CredentialEntity
        {
            Fido2CredentialId = credential.Fido2CredentialId,
            PersonId = credential.PersonId,
            CredentialId = credential.CredentialId,
            PublicKey = credential.PublicKey,
            SignatureCounter = credential.SignatureCounter,
            Aaguid = credential.Aaguid,
            Label = credential.Label,
            CreatedUtc = credential.CreatedUtc
        });

        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            EventId = Guid.NewGuid(),
            EventType = "Fido2CredentialRegistered",
            OccurredUtc = credential.CreatedUtc,
            PersonId = credential.PersonId,
            FrontendClientId = frontendClientId,
            SourceIpAddress = sourceIpAddress,
            ClientSourceIpAddress = clientSourceIpAddress,
            SourceComponent = "Api",
            CorrelationId = correlationId,
            Result = "Succeeded",
            AuthenticationMethod = "Windows",
            PolicyRequirementsSummary = "Fido2CredentialRegistration"
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateSignatureCounterAsync(Guid fido2CredentialId, long signatureCounter, CancellationToken cancellationToken)
    {
        await dbContext.Fido2Credentials
            .Where(x => x.Fido2CredentialId == fido2CredentialId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.SignatureCounter, signatureCounter), cancellationToken);
    }

    public async Task RecordStepUpSuccessAsync(Guid fido2CredentialId, Guid personId, long newSignatureCounter, DateTimeOffset occurredUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken)
    {
        await dbContext.Fido2Credentials
            .Where(x => x.Fido2CredentialId == fido2CredentialId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.SignatureCounter, newSignatureCounter), cancellationToken);

        dbContext.AuditEvents.Add(BuildStepUpAuditEvent("Fido2StepUpPasskeySucceeded", personId, occurredUtc, correlationId, frontendClientId, sourceIpAddress, clientSourceIpAddress, result: "Succeeded", failureCategory: null));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordStepUpFailureAsync(Guid personId, DateTimeOffset occurredUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken)
    {
        dbContext.AuditEvents.Add(BuildStepUpAuditEvent("Fido2StepUpPasskeyFailed", personId, occurredUtc, correlationId, frontendClientId, sourceIpAddress, clientSourceIpAddress, result: "Failed", failureCategory: "Mfa"));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static AuditEventEntity BuildStepUpAuditEvent(string eventType, Guid personId, DateTimeOffset occurredUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, string result, string? failureCategory)
        => new()
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
            Result = result,
            FailureCategory = failureCategory,
            AuthenticationMethod = "Fido2",
            PolicyRequirementsSummary = "Fido2CredentialStepUp"
        };
}
