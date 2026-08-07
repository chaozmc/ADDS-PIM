using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Application.Mfa;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

/// <summary>
/// Verification-time TOTP state. Unlike <see cref="EfTotpFactorEnrollmentStore"/>/
/// <see cref="EfTotpEnrollmentConfirmationStore"/>, this store reads the already-active factor and
/// writes the anti-replay ledger, lockout state, and the corresponding audit events &mdash; following
/// EfMembershipRequestStateStore's pattern of building AuditEventEntity directly from primitives
/// rather than introducing a bespoke Application-level audit-event record.
/// </summary>
public sealed class EfTotpVerificationStore(PimDbContext dbContext) : ITotpVerificationStore
{
    public Task<ActiveTotpFactor?> FindActiveAsync(Guid personId, CancellationToken cancellationToken)
        => dbContext.TotpFactors.AsNoTracking()
            .Where(x => x.PersonId == personId && x.IsActive && x.RevokedUtc == null)
            .Select(x => new ActiveTotpFactor(x.TotpFactorId, x.PersonId, x.EncryptedSecret, x.ProtectionKeyId, x.LastUsedTimeStep, x.LockedUntilUtc))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task RecordSuccessAsync(Guid totpFactorId, Guid personId, Guid mfaTransactionId, Guid requestId, long timeStep, DateTimeOffset usedUtc, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken)
    {
        var factor = await dbContext.TotpFactors.SingleAsync(x => x.TotpFactorId == totpFactorId, cancellationToken);
        factor.LastUsedTimeStep = timeStep;
        factor.ConsecutiveFailedAttempts = 0;
        factor.LockedUntilUtc = null;

        dbContext.TotpUsedTimeSteps.Add(new TotpUsedTimeStepEntity { TotpFactorId = totpFactorId, TimeStep = timeStep, UsedUtc = usedUtc, MfaTransactionId = mfaTransactionId });

        var request = await dbContext.MembershipRequests.AsNoTracking().SingleAsync(x => x.RequestId == requestId, cancellationToken);
        dbContext.AuditEvents.Add(BuildAuditEvent("TotpVerificationSucceeded", usedUtc, personId, requestId, request, auditContext, result: "Succeeded", failureCategory: null));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<TotpFailureOutcome> RecordFailureAsync(Guid totpFactorId, Guid personId, Guid mfaTransactionId, Guid requestId, DateTimeOffset failedUtc, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken)
    {
        var factor = await dbContext.TotpFactors.SingleAsync(x => x.TotpFactorId == totpFactorId, cancellationToken);
        factor.ConsecutiveFailedAttempts++;
        var justLocked = factor.ConsecutiveFailedAttempts >= 5;
        if (justLocked)
        {
            factor.LockedUntilUtc = failedUtc.AddMinutes(15);
        }

        var request = await dbContext.MembershipRequests.AsNoTracking().SingleAsync(x => x.RequestId == requestId, cancellationToken);
        dbContext.AuditEvents.Add(BuildAuditEvent(justLocked ? "TotpFactorLocked" : "TotpVerificationFailed", failedUtc, personId, requestId, request, auditContext, result: "Failed", failureCategory: "Mfa"));

        await dbContext.SaveChangesAsync(cancellationToken);
        return new TotpFailureOutcome(factor.ConsecutiveFailedAttempts, factor.LockedUntilUtc);
    }

    public async Task RecordRejectionAsync(Guid personId, Guid requestId, string eventType, DateTimeOffset occurredUtc, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken)
    {
        var request = await dbContext.MembershipRequests.AsNoTracking().SingleAsync(x => x.RequestId == requestId, cancellationToken);
        dbContext.AuditEvents.Add(BuildAuditEvent(eventType, occurredUtc, personId, requestId, request, auditContext, result: "Failed", failureCategory: "Mfa"));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordStepUpSuccessAsync(Guid totpFactorId, Guid personId, long timeStep, DateTimeOffset usedUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken)
    {
        var factor = await dbContext.TotpFactors.SingleAsync(x => x.TotpFactorId == totpFactorId, cancellationToken);
        factor.LastUsedTimeStep = timeStep;
        factor.ConsecutiveFailedAttempts = 0;
        factor.LockedUntilUtc = null;

        dbContext.AuditEvents.Add(BuildStepUpAuditEvent("Fido2StepUpTotpSucceeded", usedUtc, personId, correlationId, frontendClientId, sourceIpAddress, clientSourceIpAddress, result: "Succeeded", failureCategory: null));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<TotpFailureOutcome> RecordStepUpFailureAsync(Guid totpFactorId, Guid personId, DateTimeOffset failedUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken)
    {
        var factor = await dbContext.TotpFactors.SingleAsync(x => x.TotpFactorId == totpFactorId, cancellationToken);
        factor.ConsecutiveFailedAttempts++;
        var justLocked = factor.ConsecutiveFailedAttempts >= 5;
        if (justLocked)
        {
            factor.LockedUntilUtc = failedUtc.AddMinutes(15);
        }

        dbContext.AuditEvents.Add(BuildStepUpAuditEvent(justLocked ? "TotpFactorLocked" : "Fido2StepUpTotpFailed", failedUtc, personId, correlationId, frontendClientId, sourceIpAddress, clientSourceIpAddress, result: "Failed", failureCategory: "Mfa"));
        await dbContext.SaveChangesAsync(cancellationToken);
        return new TotpFailureOutcome(factor.ConsecutiveFailedAttempts, factor.LockedUntilUtc);
    }

    private static AuditEventEntity BuildStepUpAuditEvent(string eventType, DateTimeOffset occurredUtc, Guid personId, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, string result, string? failureCategory)
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
            AuthenticationMethod = "Totp",
            PolicyRequirementsSummary = "Fido2CredentialStepUp"
        };

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
