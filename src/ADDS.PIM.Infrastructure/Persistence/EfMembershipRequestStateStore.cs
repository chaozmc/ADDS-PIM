using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Domain.MembershipRequests;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfMembershipRequestStateStore(PimDbContext dbContext, TimeProvider timeProvider) : IMembershipRequestStateStore
{
    public async Task TransitionAsync(Guid requestId, MembershipRequestStatus expectedStatus, MembershipRequestStatus nextStatus, MembershipRequestTransitionAuditContext auditContext, string reason, CancellationToken cancellationToken)
    {
        if (!MembershipRequestStateMachine.CanTransition(expectedStatus, nextStatus))
            throw new InvalidOperationException($"Illegal membership-request transition: {expectedStatus} to {nextStatus}.");

        var request = await dbContext.MembershipRequests.SingleOrDefaultAsync(x => x.RequestId == requestId, cancellationToken)
            ?? throw new InvalidOperationException("Membership request does not exist.");
        if (request.Status != expectedStatus)
            throw new InvalidOperationException("Membership request state changed concurrently.");

        var occurredUtc = timeProvider.GetUtcNow();
        request.Status = nextStatus;
        // An administrator-triggered transition (e.g. orphaned-request cleanup) attributes the audit trail to
        // the administrator who acted, not the original requester whose request is being terminated. Detected
        // by presence, not just AdministratorAccountId, since an administrator without a local DirectoryAccount
        // row still resolves to a display name only (EfAdministrationDataStore.ResolveAdministratorAsync).
        var isAdministratorTriggered = auditContext.AdministratorAccountId is not null || auditContext.AdministratorDisplayName is not null;
        Guid? actorAccountId = isAdministratorTriggered ? auditContext.AdministratorAccountId : request.ActorAccountId;
        var actorDisplayNameSnapshot = isAdministratorTriggered ? auditContext.AdministratorDisplayName : request.ActorAccountDisplayNameSnapshot;
        dbContext.MembershipRequestStatusHistory.Add(new MembershipRequestStatusHistoryEntity
        {
            EntryId = Guid.NewGuid(), RequestId = requestId, PreviousStatus = expectedStatus, NewStatus = nextStatus,
            OccurredUtc = occurredUtc, ActorId = actorAccountId?.ToString("D") ?? actorDisplayNameSnapshot ?? "Administrator", SourceComponent = "Api", Reason = reason
        });
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            EventId = Guid.NewGuid(), EventType = $"MembershipRequest{nextStatus}", OccurredUtc = occurredUtc,
            PersonId = request.PersonId, ActorAccountId = actorAccountId, TargetAccountId = request.TargetAccountId,
            PersonDisplayNameSnapshot = request.PersonDisplayNameSnapshot, ActorAccountDisplayNameSnapshot = actorDisplayNameSnapshot,
            TargetAccountDisplayNameSnapshot = request.TargetAccountDisplayNameSnapshot, TargetGroupDisplayNameSnapshot = request.TargetGroupDisplayNameSnapshot,
            FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress, ClientSourceIpAddress = auditContext.ClientSourceIpAddress, SourceComponent = "Api",
            CorrelationId = auditContext.CorrelationId, RequestId = request.RequestId, TargetGroupId = request.TargetGroupId,
            RequestedTtlSeconds = request.RequestedTtlSeconds, Result = nextStatus is MembershipRequestStatus.Failed or MembershipRequestStatus.Rejected ? "Failed" : "Succeeded",
            FailureCategory = auditContext.FailureCategory, AuthenticationMethod = auditContext.AuthenticationMethod, PolicyRequirementsSummary = auditContext.PolicyRequirementsSummary
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
