using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Contracts.MembershipRequests.V1;
using ADDS.PIM.Domain.MembershipRequests;

namespace ADDS.PIM.Application.MembershipRequests;

public sealed record PendingApprovalRequestDetails(
    Guid RequestId,
    Guid TargetAccountId,
    Guid TargetGroupId,
    long RequestedTtlSeconds,
    string? TicketReference,
    Guid ActorDirectoryScopeId,
    Guid ActorObjectGuid,
    MembershipRequestStatus Status);

public interface IApprovalDataSource
{
    Task<PendingApprovalsPage> ListPendingApprovalsAsync(IReadOnlyList<Guid> approvableGroupIds, int pageNumber, int pageSize, CancellationToken cancellationToken);

    /// <summary>Loads the request's execution-relevant fields plus the original requester's actor-account directory identity, needed to re-run authorization as that requester (never as the approver).</summary>
    Task<PendingApprovalRequestDetails?> GetPendingRequestAsync(Guid requestId, CancellationToken cancellationToken);
}

public enum ApprovalDecisionOutcome { Accepted, NotFound, Forbidden, NotEligible, Invalid }

public sealed record ApprovalDecisionResult(ApprovalDecisionOutcome Outcome, MembershipRequestExecutionResult? Execution);

/// <summary>
/// Read model for the requests currently awaiting the authenticated Actor's decision.
/// Never used as authorization evidence: approve/reject re-check eligibility fresh.
/// </summary>
public sealed class ListPendingApprovalsUseCase(
    IGroupApprovalAuthorizer approvalAuthorizer,
    IApprovalDataSource approvalDataSource)
{
    public async Task<PendingApprovalsPage?> ExecuteAsync(AuthenticatedDirectoryAccount actor, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        if (actor.DirectoryScopeId == Guid.Empty || actor.ObjectGuid == Guid.Empty || pageNumber < 1 || pageSize is not (10 or 20 or 30 or 40 or 50))
        {
            return null;
        }

        var approver = await approvalAuthorizer.ResolveApproverAsync(actor, cancellationToken);
        if (approver is null)
        {
            return new PendingApprovalsPage([], 0);
        }

        var approvableGroupIds = await approvalAuthorizer.ListApprovableGroupIdsAsync(approver.PersonId, cancellationToken);
        return approvableGroupIds.Count == 0
            ? new PendingApprovalsPage([], 0)
            : await approvalDataSource.ListPendingApprovalsAsync(approvableGroupIds, pageNumber, pageSize, cancellationToken);
    }
}

/// <summary>Cheap check backing the Web nav item's visibility; never authoritative on its own - the page and API independently re-verify.</summary>
public sealed class CheckApproverEligibilityUseCase(IGroupApprovalAuthorizer approvalAuthorizer)
{
    public async Task<bool> ExecuteAsync(AuthenticatedDirectoryAccount actor, CancellationToken cancellationToken)
        => actor.DirectoryScopeId != Guid.Empty && actor.ObjectGuid != Guid.Empty
            && await approvalAuthorizer.ResolveApproverAsync(actor, cancellationToken) is not null;
}

public sealed class ApproveMembershipRequestUseCase(
    IGroupApprovalAuthorizer approvalAuthorizer,
    IApprovalDataSource approvalDataSource,
    ExecuteMembershipRequestUseCase executor)
{
    public async Task<ApprovalDecisionResult> ExecuteAsync(Guid requestId, AuthenticatedDirectoryAccount approverActor, Guid correlationId, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty || approverActor.DirectoryScopeId == Guid.Empty || approverActor.ObjectGuid == Guid.Empty)
        {
            return new(ApprovalDecisionOutcome.Invalid, null);
        }

        var approver = await approvalAuthorizer.ResolveApproverAsync(approverActor, cancellationToken);
        if (approver is null)
        {
            return new(ApprovalDecisionOutcome.Forbidden, null);
        }

        var request = await approvalDataSource.GetPendingRequestAsync(requestId, cancellationToken);
        if (request is null)
        {
            return new(ApprovalDecisionOutcome.NotFound, null);
        }

        if (request.Status != MembershipRequestStatus.AwaitingApproval)
        {
            return new(ApprovalDecisionOutcome.NotEligible, null);
        }

        // Re-checked here, immediately before the decision takes effect - never trusted from a prior list view.
        if (!await approvalAuthorizer.IsApproverForGroupAsync(approver.PersonId, request.TargetGroupId, cancellationToken))
        {
            return new(ApprovalDecisionOutcome.Forbidden, null);
        }

        var command = new ExecuteMembershipRequestCommand(
            requestId,
            correlationId,
            new AuthenticatedDirectoryAccount(request.ActorDirectoryScopeId, request.ActorObjectGuid),
            request.TargetAccountId,
            request.TargetGroupId,
            request.RequestedTtlSeconds,
            request.TicketReference,
            auditContext with { AdministratorAccountId = approver.AccountId, AdministratorDisplayName = approver.DisplayName, DecidingApproverPersonId = approver.PersonId, DecidingApproverDisplayName = approver.DisplayName });

        var execution = await executor.ExecuteFromApprovedAsync(command, cancellationToken);
        return new(ApprovalDecisionOutcome.Accepted, execution);
    }
}

public sealed class RejectMembershipRequestUseCase(
    IGroupApprovalAuthorizer approvalAuthorizer,
    IApprovalDataSource approvalDataSource,
    IMembershipRequestStateStore stateStore)
{
    public async Task<ApprovalDecisionOutcome> ExecuteAsync(Guid requestId, AuthenticatedDirectoryAccount approverActor, string rejectionReason, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty || approverActor.DirectoryScopeId == Guid.Empty || approverActor.ObjectGuid == Guid.Empty || string.IsNullOrWhiteSpace(rejectionReason))
        {
            return ApprovalDecisionOutcome.Invalid;
        }

        var approver = await approvalAuthorizer.ResolveApproverAsync(approverActor, cancellationToken);
        if (approver is null)
        {
            return ApprovalDecisionOutcome.Forbidden;
        }

        var request = await approvalDataSource.GetPendingRequestAsync(requestId, cancellationToken);
        if (request is null)
        {
            return ApprovalDecisionOutcome.NotFound;
        }

        if (request.Status != MembershipRequestStatus.AwaitingApproval)
        {
            return ApprovalDecisionOutcome.NotEligible;
        }

        if (!await approvalAuthorizer.IsApproverForGroupAsync(approver.PersonId, request.TargetGroupId, cancellationToken))
        {
            return ApprovalDecisionOutcome.Forbidden;
        }

        try
        {
            await stateStore.TransitionAsync(
                requestId,
                MembershipRequestStatus.AwaitingApproval,
                MembershipRequestStatus.Rejected,
                auditContext with { FailureCategory = "ApproverRejected", AdministratorAccountId = approver.AccountId, AdministratorDisplayName = approver.DisplayName, DecidingApproverPersonId = approver.PersonId, DecidingApproverDisplayName = approver.DisplayName },
                rejectionReason.Trim(),
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Concurrently changed (e.g. a timeout cleanup or the other approver already decided).
            return ApprovalDecisionOutcome.NotEligible;
        }

        return ApprovalDecisionOutcome.Accepted;
    }
}
