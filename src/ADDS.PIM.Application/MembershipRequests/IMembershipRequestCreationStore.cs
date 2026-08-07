using ADDS.PIM.Domain.MembershipRequests;

namespace ADDS.PIM.Application.MembershipRequests;

/// <summary>
/// Persists a request, its initial status history, and its audit event as one
/// atomic operation.
/// </summary>
public interface IMembershipRequestCreationStore
{
    Task CreateAsync(
        MembershipRequest request,
        MembershipRequestStatusHistoryEntry statusHistory,
        MembershipRequestCreatedAuditEvent auditEvent,
        CancellationToken cancellationToken);
}
