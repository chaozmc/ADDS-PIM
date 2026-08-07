namespace ADDS.PIM.Contracts.MembershipRequests.V1;

/// <summary>
/// Versioned, signed query for the request history visible to the authenticated
/// Actor account. The API resolves the person server-side; this body never
/// carries a person or request-owner identifier.
/// </summary>
public sealed record QueryMyMembershipRequests(
    Guid ActorDirectoryScopeId,
    Guid ActorObjectGuid,
    int PageNumber,
    int PageSize,
    int? Year,
    int? Month,
    Guid? EntitlementId,
    Guid? TargetGroupId);

public sealed record MyMembershipRequestsPage(
    IReadOnlyList<MyMembershipRequest> Items,
    int TotalCount,
    IReadOnlyList<int> AvailableYears,
    IReadOnlyList<MyMembershipRequestFilterOption> Entitlements,
    IReadOnlyList<MyMembershipRequestFilterOption> TargetGroups);

public sealed record MyMembershipRequestFilterOption(Guid Id, string DisplayName);

public sealed record MyMembershipRequest(
    Guid RequestId,
    Guid EntitlementId,
    Guid TargetGroupId,
    string TargetAccountDisplayName,
    string TargetGroupDisplayName,
    long RequestedTtlSeconds,
    DateTimeOffset CreatedUtc,
    string Status,
    string Reason,
    string? TicketReference);
