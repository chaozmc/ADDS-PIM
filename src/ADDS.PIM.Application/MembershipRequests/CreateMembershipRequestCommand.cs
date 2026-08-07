namespace ADDS.PIM.Application.MembershipRequests;

/// <summary>
/// Internal command built only after the caller has authenticated the user and
/// technical client. It is not an HTTP transport contract.
/// </summary>
public sealed record CreateMembershipRequestCommand(
    Guid RequestId,
    Guid CorrelationId,
    Guid PersonId,
    Guid ActorAccountId,
    Guid TargetAccountId,
    Guid EntitlementId,
    string FrontendClientId,
    string AuthenticationMethod,
    string PolicyRequirementsSummary,
    string? SourceIpAddress,
    Guid TargetGroupId,
    string PersonDisplayNameSnapshot,
    string ActorAccountDisplayNameSnapshot,
    string TargetAccountDisplayNameSnapshot,
    string TargetGroupDisplayNameSnapshot,
    long RequestedTtlSeconds,
    string Reason,
    string? TicketReference);
