namespace ADDS.PIM.Contracts.MembershipRequests.V1;

/// <summary>
/// Versioned, signed query for the requests awaiting the authenticated Actor's
/// decision as an approver. The API resolves the person and their approver-eligible
/// groups server-side; this body never carries a person or group identifier.
/// </summary>
public sealed record QueryPendingApprovals(
    Guid ActorDirectoryScopeId,
    Guid ActorObjectGuid,
    int PageNumber,
    int PageSize);

public sealed record PendingApprovalsPage(
    IReadOnlyList<PendingApprovalItem> Items,
    int TotalCount);

public sealed record PendingApprovalItem(
    Guid RequestId,
    Guid TargetGroupId,
    string TargetGroupDisplayName,
    string PersonDisplayName,
    string TargetAccountDisplayName,
    long RequestedTtlSeconds,
    DateTimeOffset CreatedUtc,
    string Reason,
    string? TicketReference);

public sealed record ApproveMembershipRequest(
    Guid ActorDirectoryScopeId,
    Guid ActorObjectGuid);

public sealed record RejectMembershipRequest(
    Guid ActorDirectoryScopeId,
    Guid ActorObjectGuid,
    string RejectionReason);

public sealed record ApprovalDecisionAccepted(
    Guid RequestId,
    string Status,
    string? OutcomeMessage);

/// <summary>Cheap eligibility check backing the Web nav item; never authoritative on its own.</summary>
public sealed record QueryApproverEligibility(
    Guid ActorDirectoryScopeId,
    Guid ActorObjectGuid);

public sealed record ApproverEligibilityResponse(bool IsApprover);
