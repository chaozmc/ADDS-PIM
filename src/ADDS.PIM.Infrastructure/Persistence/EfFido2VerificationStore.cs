using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Application.Mfa;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

/// <summary>Mirrors <see cref="EfTotpVerificationStore"/>'s audit-event-from-primitives pattern for FIDO2 second-factor confirmations.</summary>
public sealed class EfFido2VerificationStore(PimDbContext dbContext) : IFido2VerificationStore
{
    public async Task RecordSuccessAsync(Guid fido2CredentialId, Guid personId, long newSignatureCounter, Guid requestId, DateTimeOffset usedUtc, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken)
    {
        await dbContext.Fido2Credentials
            .Where(x => x.Fido2CredentialId == fido2CredentialId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.SignatureCounter, newSignatureCounter), cancellationToken);

        var request = await dbContext.MembershipRequests.AsNoTracking().SingleAsync(x => x.RequestId == requestId, cancellationToken);
        dbContext.AuditEvents.Add(BuildAuditEvent("Fido2AssertionSucceeded", usedUtc, personId, requestId, request, auditContext, result: "Succeeded", failureCategory: null));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordRejectionAsync(Guid personId, Guid requestId, string eventType, DateTimeOffset occurredUtc, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken)
    {
        var request = await dbContext.MembershipRequests.AsNoTracking().SingleAsync(x => x.RequestId == requestId, cancellationToken);
        dbContext.AuditEvents.Add(BuildAuditEvent(eventType, occurredUtc, personId, requestId, request, auditContext, result: "Failed", failureCategory: "Mfa"));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static AuditEventEntity BuildAuditEvent(string eventType, DateTimeOffset occurredUtc, Guid personId, Guid requestId, MembershipRequestEntity request, MembershipRequestTransitionAuditContext auditContext, string result, string? failureCategory)
        => new()
        {
            EventId = Guid.NewGuid(),
            EventType = eventType,
            OccurredUtc = occurredUtc,
            PersonId = personId,
            ActorAccountId = request.ActorAccountId,
            TargetAccountId = request.TargetAccountId,
            PersonDisplayNameSnapshot = request.PersonDisplayNameSnapshot,
            ActorAccountDisplayNameSnapshot = request.ActorAccountDisplayNameSnapshot,
            TargetAccountDisplayNameSnapshot = request.TargetAccountDisplayNameSnapshot,
            TargetGroupDisplayNameSnapshot = request.TargetGroupDisplayNameSnapshot,
            FrontendClientId = auditContext.FrontendClientId,
            SourceIpAddress = auditContext.SourceIpAddress,
            ClientSourceIpAddress = auditContext.ClientSourceIpAddress,
            SourceComponent = "Api",
            CorrelationId = auditContext.CorrelationId,
            RequestId = requestId,
            TargetGroupId = request.TargetGroupId,
            RequestedTtlSeconds = request.RequestedTtlSeconds,
            Result = result,
            FailureCategory = failureCategory,
            AuthenticationMethod = auditContext.AuthenticationMethod,
            PolicyRequirementsSummary = auditContext.PolicyRequirementsSummary
        };
}
