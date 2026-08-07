using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Domain.MembershipRequests;

namespace ADDS.PIM.Api.Development;

/// <summary>
/// DEV-only sink for exercising the real application request-creation flow.
/// It intentionally persists and executes nothing.
/// </summary>
public sealed class DevelopmentNoOpMembershipRequestCreationStore : IMembershipRequestCreationStore
{
    public Task CreateAsync(
        MembershipRequest request,
        MembershipRequestStatusHistoryEntry statusHistory,
        MembershipRequestCreatedAuditEvent auditEvent,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
