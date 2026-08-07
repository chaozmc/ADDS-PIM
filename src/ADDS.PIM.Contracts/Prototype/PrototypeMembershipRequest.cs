namespace ADDS.PIM.Contracts.Prototype;

/// <summary>
/// Development-only transport contract for exercising the MVP user interface.
/// It must not be used for authorization or Active Directory execution.
/// </summary>
public sealed record PrototypeMembershipRequest(
    Guid RequestId,
    Guid TargetGroupId,
    long RequestedTtlSeconds,
    string Reason,
    string? TicketReference);
