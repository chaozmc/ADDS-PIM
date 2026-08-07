namespace ADDS.PIM.Contracts.MembershipRequests.V1;

/// <summary>
/// Versioned Web-to-API request body. The request signature authenticates this
/// complete body; the API still re-evaluates its contents against current SQL
/// authorization facts.
/// </summary>
public sealed record CreateMembershipRequest(
    Guid ActorDirectoryScopeId,
    Guid ActorObjectGuid,
    Guid TargetAccountId,
    Guid TargetGroupId,
    long RequestedTtlSeconds,
    string Reason,
    string? TicketReference);

public sealed record MembershipRequestAccepted(
    Guid RequestId,
    Guid CorrelationId,
    string Status,
    DateTimeOffset CreatedUtc,
    string? OutcomeMessage);
