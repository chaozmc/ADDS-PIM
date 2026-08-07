using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Domain.MembershipRequests;
using ADDS.PIM.Infrastructure.Persistence.Entities;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfMembershipRequestCreationStore(PimDbContext dbContext) : IMembershipRequestCreationStore
{
    public async Task CreateAsync(
        MembershipRequest request,
        MembershipRequestStatusHistoryEntry statusHistory,
        MembershipRequestCreatedAuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        dbContext.MembershipRequests.Add(new MembershipRequestEntity
        {
            RequestId = request.RequestId,
            PersonId = request.PersonId,
            ActorAccountId = request.ActorAccountId,
            TargetAccountId = request.TargetAccountId,
            TargetGroupId = request.TargetGroupId,
            EntitlementId = request.EntitlementId,
            PersonDisplayNameSnapshot = auditEvent.PersonDisplayNameSnapshot,
            ActorAccountDisplayNameSnapshot = auditEvent.ActorAccountDisplayNameSnapshot,
            TargetAccountDisplayNameSnapshot = auditEvent.TargetAccountDisplayNameSnapshot,
            TargetGroupDisplayNameSnapshot = auditEvent.TargetGroupDisplayNameSnapshot,
            RequestedTtlSeconds = request.RequestedTtlSeconds,
            Reason = request.Reason,
            TicketReference = request.TicketReference,
            CreatedUtc = request.CreatedUtc,
            Status = request.Status
        });

        dbContext.MembershipRequestStatusHistory.Add(new MembershipRequestStatusHistoryEntity
        {
            EntryId = statusHistory.EntryId,
            RequestId = statusHistory.RequestId,
            PreviousStatus = statusHistory.PreviousStatus,
            NewStatus = statusHistory.NewStatus,
            OccurredUtc = statusHistory.OccurredUtc,
            ActorId = statusHistory.ActorId,
            SourceComponent = statusHistory.SourceComponent,
            Reason = statusHistory.Reason
        });

        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            EventId = auditEvent.EventId,
            EventType = "MembershipRequestCreated",
            OccurredUtc = auditEvent.OccurredUtc,
            PersonId = auditEvent.PersonId,
            ActorAccountId = auditEvent.ActorAccountId,
            TargetAccountId = auditEvent.TargetAccountId,
            PersonDisplayNameSnapshot = auditEvent.PersonDisplayNameSnapshot,
            ActorAccountDisplayNameSnapshot = auditEvent.ActorAccountDisplayNameSnapshot,
            TargetAccountDisplayNameSnapshot = auditEvent.TargetAccountDisplayNameSnapshot,
            TargetGroupDisplayNameSnapshot = auditEvent.TargetGroupDisplayNameSnapshot,
            FrontendClientId = auditEvent.FrontendClientId,
            SourceIpAddress = auditEvent.SourceIpAddress,
            SourceComponent = "Api",
            CorrelationId = auditEvent.CorrelationId,
            RequestId = auditEvent.RequestId,
            TargetGroupId = auditEvent.TargetGroupId,
            RequestedTtlSeconds = auditEvent.RequestedTtlSeconds,
            Result = "Succeeded",
            AuthenticationMethod = auditEvent.AuthenticationMethod,
            PolicyRequirementsSummary = auditEvent.PolicyRequirementsSummary
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
