namespace ADDS.PIM.Application.MembershipRequests;

public sealed record MembershipRequestCreatedAuditEvent(
    Guid EventId,
    DateTimeOffset OccurredUtc,
    Guid CorrelationId,
    Guid RequestId,
    Guid PersonId,
    Guid ActorAccountId,
    Guid TargetAccountId,
    string PersonDisplayNameSnapshot,
    string ActorAccountDisplayNameSnapshot,
    string TargetAccountDisplayNameSnapshot,
    string TargetGroupDisplayNameSnapshot,
    string FrontendClientId,
    string? SourceIpAddress,
    Guid TargetGroupId,
    long RequestedTtlSeconds,
    string AuthenticationMethod,
    string PolicyRequirementsSummary);
