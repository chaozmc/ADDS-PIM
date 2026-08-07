using ADDS.PIM.Domain.MembershipRequests;

namespace ADDS.PIM.Application.MembershipRequests;

public sealed class CreateMembershipRequestUseCase(
    IMembershipRequestCreationStore store,
    TimeProvider timeProvider)
{
    public async Task<MembershipRequest> ExecuteAsync(
        CreateMembershipRequestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        ValidateTrustedContext(command);

        var createdUtc = timeProvider.GetUtcNow();
        var request = new MembershipRequest(
            command.RequestId,
            command.PersonId,
            command.ActorAccountId,
            command.TargetAccountId,
            command.TargetGroupId,
            command.EntitlementId,
            command.RequestedTtlSeconds,
            command.Reason,
            command.TicketReference,
            createdUtc);

        var statusHistory = new MembershipRequestStatusHistoryEntry(
            Guid.NewGuid(),
            request.RequestId,
            null,
            MembershipRequestStatus.Created,
            createdUtc,
            command.ActorAccountId.ToString("D"),
            "Api",
            "Request created.");

        var auditEvent = new MembershipRequestCreatedAuditEvent(
            Guid.NewGuid(),
            createdUtc,
            command.CorrelationId,
            request.RequestId,
            command.PersonId,
            command.ActorAccountId,
            command.TargetAccountId,
            command.PersonDisplayNameSnapshot,
            command.ActorAccountDisplayNameSnapshot,
            command.TargetAccountDisplayNameSnapshot,
            command.TargetGroupDisplayNameSnapshot,
            command.FrontendClientId,
            command.SourceIpAddress,
            request.TargetGroupId,
            request.RequestedTtlSeconds,
            command.AuthenticationMethod,
            command.PolicyRequirementsSummary);

        await store.CreateAsync(request, statusHistory, auditEvent, cancellationToken);
        return request;
    }

    private static void ValidateTrustedContext(CreateMembershipRequestCommand command)
    {
        if (command.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("A correlation ID is required.", nameof(command));
        }

        if (command.PersonId == Guid.Empty || command.ActorAccountId == Guid.Empty || command.TargetAccountId == Guid.Empty || command.TargetGroupId == Guid.Empty || command.EntitlementId == Guid.Empty
            || string.IsNullOrWhiteSpace(command.PersonDisplayNameSnapshot)
            || string.IsNullOrWhiteSpace(command.ActorAccountDisplayNameSnapshot)
            || string.IsNullOrWhiteSpace(command.TargetAccountDisplayNameSnapshot)
            || string.IsNullOrWhiteSpace(command.TargetGroupDisplayNameSnapshot)
            || string.IsNullOrWhiteSpace(command.FrontendClientId)
            || string.IsNullOrWhiteSpace(command.AuthenticationMethod)
            || string.IsNullOrWhiteSpace(command.PolicyRequirementsSummary))
        {
            throw new ArgumentException("Trusted caller context is incomplete.", nameof(command));
        }
    }
}
