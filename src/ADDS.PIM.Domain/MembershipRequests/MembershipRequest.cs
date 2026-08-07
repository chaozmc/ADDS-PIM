namespace ADDS.PIM.Domain.MembershipRequests;

/// <summary>
/// The domain representation of a membership request's lifecycle.
/// Persistence of history, actor, reason, and concurrency metadata is owned by
/// the application and infrastructure layers.
/// </summary>
public sealed class MembershipRequest
{
    public MembershipRequest(
        Guid requestId,
        Guid personId,
        Guid actorAccountId,
        Guid targetAccountId,
        Guid targetGroupId,
        Guid entitlementId,
        long requestedTtlSeconds,
        string reason,
        string? ticketReference,
        DateTimeOffset createdUtc)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("A membership request ID is required.", nameof(requestId));
        }

        if (personId == Guid.Empty || actorAccountId == Guid.Empty || targetAccountId == Guid.Empty || entitlementId == Guid.Empty)
        {
            throw new ArgumentException("The person, actor account, target account, and entitlement are required.");
        }

        if (targetGroupId == Guid.Empty)
        {
            throw new ArgumentException("A target group ID is required.", nameof(targetGroupId));
        }

        if (requestedTtlSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedTtlSeconds), "Requested TTL must be positive.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A request reason is required.", nameof(reason));
        }

        RequestId = requestId;
        PersonId = personId;
        ActorAccountId = actorAccountId;
        TargetAccountId = targetAccountId;
        TargetGroupId = targetGroupId;
        EntitlementId = entitlementId;
        RequestedTtlSeconds = requestedTtlSeconds;
        Reason = reason.Trim();
        TicketReference = ticketReference;
        CreatedUtc = createdUtc;
    }

    public Guid RequestId { get; }

    public Guid PersonId { get; }

    public Guid ActorAccountId { get; }

    public Guid TargetAccountId { get; }

    public Guid TargetGroupId { get; }

    public Guid EntitlementId { get; }

    public long RequestedTtlSeconds { get; }

    public string Reason { get; }

    public string? TicketReference { get; }

    public DateTimeOffset CreatedUtc { get; }

    public MembershipRequestStatus Status { get; private set; } = MembershipRequestStatus.Created;

    public void TransitionTo(MembershipRequestStatus requestedStatus)
    {
        if (!MembershipRequestStateMachine.CanTransition(Status, requestedStatus))
        {
            throw new MembershipRequestTransitionException(Status, requestedStatus);
        }

        Status = requestedStatus;
    }
}
