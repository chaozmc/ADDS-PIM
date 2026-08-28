using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Application.Worker;
using ADDS.PIM.Domain.MembershipRequests;

namespace ADDS.PIM.Application.MembershipRequests;

public interface IMembershipRequestStateStore
{
    Task TransitionAsync(Guid requestId, MembershipRequestStatus expectedStatus, MembershipRequestStatus nextStatus, MembershipRequestTransitionAuditContext auditContext, string reason, CancellationToken cancellationToken);
}

public sealed record MembershipRequestTransitionAuditContext(
    Guid CorrelationId,
    string FrontendClientId,
    string AuthenticationMethod,
    string PolicyRequirementsSummary,
    string? SourceIpAddress,
    string? FailureCategory = null,
    // Set only for an administrator-triggered transition (e.g. orphaned-request cleanup) - overrides the
    // default attribution of the original requester's actor account on the resulting audit trail.
    Guid? AdministratorAccountId = null,
    string? AdministratorDisplayName = null,
    // The browser's own remote IP (Web-captured, forwarded via X-Client-Ip) - informational only, distinct
    // from SourceIpAddress (the Web frontend instance's own IP as seen by the API).
    string? ClientSourceIpAddress = null,
    // Set only when this transition is an explicit approve/reject decision (never for e.g. orphaned-request
    // cleanup) - lets EfMembershipRequestStateStore notify the group's other approvers of the decision while
    // excluding the one who made it. Deliberately separate from AdministratorAccountId/AdministratorDisplayName
    // above (a DirectoryAccountId, not a PersonId) since resolving Person from that would require an extra join.
    Guid? DecidingApproverPersonId = null,
    string? DecidingApproverDisplayName = null);

public sealed record ExecuteMembershipRequestCommand(
    Guid RequestId,
    Guid CorrelationId,
    AuthenticatedDirectoryAccount Actor,
    Guid TargetAccountId,
    Guid TargetGroupId,
    long RequestedTtlSeconds,
    string? TicketReference,
    MembershipRequestTransitionAuditContext AuditContext);

public sealed record MembershipRequestExecutionResult(MembershipRequestStatus Status, string? OutcomeMessage);

public static class MembershipRequestUserMessages
{
    public static string? ForWorkerResult(WorkerMembershipDispatchResult result)
        => result.MembershipResult?.Kind == TemporaryGroupMembershipResultKind.ExistingMembership
            ? "Das Zielkonto ist bereits Mitglied dieser Gruppe. Eine bestehende zeitlich begrenzte oder permanente Mitgliedschaft wird nicht verlängert oder ersetzt."
            : null;
}

/// <summary>
/// Runs the execution path after the request has been durably created and,
/// for policies requiring a second factor, after that factor has been
/// verified. Authorization is deliberately evaluated again immediately
/// before the worker command is sent.
/// </summary>
public sealed class ExecuteMembershipRequestUseCase(
    AuthorizeMembershipRequestUseCase authorize,
    TicketReferenceValidator ticketValidator,
    IMembershipRequestStateStore stateStore,
    IWorkerMembershipClient workerClient)
{
    /// <summary>The no-MFA path: starts from <see cref="MembershipRequestStatus.Created"/> and rejects if the policy requires a second factor.</summary>
    public Task<MembershipRequestExecutionResult> ExecuteAsync(ExecuteMembershipRequestCommand command, CancellationToken cancellationToken)
        => ExecuteFromAsync(command, MembershipRequestStatus.Created, cancellationToken);

    /// <summary>Continues a request whose required second factor was already verified (AwaitingSecondFactor -&gt; SecondFactorValidated).</summary>
    public Task<MembershipRequestExecutionResult> ExecuteFromSecondFactorValidatedAsync(ExecuteMembershipRequestCommand command, CancellationToken cancellationToken)
        => ExecuteFromAsync(command, MembershipRequestStatus.SecondFactorValidated, cancellationToken);

    /// <summary>Continues a request an approver has just approved (AwaitingApproval -&gt; Queued). Authorization and the ticket are re-evaluated exactly as any other resume path.</summary>
    public Task<MembershipRequestExecutionResult> ExecuteFromApprovedAsync(ExecuteMembershipRequestCommand command, CancellationToken cancellationToken)
        => ExecuteFromAsync(command, MembershipRequestStatus.AwaitingApproval, cancellationToken);

    private async Task<MembershipRequestExecutionResult> ExecuteFromAsync(ExecuteMembershipRequestCommand command, MembershipRequestStatus fromStatus, CancellationToken cancellationToken)
    {
        var ticketResult = await ticketValidator.ValidateAsync(command.TargetGroupId, command.TicketReference, cancellationToken);
        if (ticketResult != TicketReferenceValidationResult.Valid)
        {
            await stateStore.TransitionAsync(command.RequestId, fromStatus, MembershipRequestStatus.Rejected, command.AuditContext with { FailureCategory = ticketResult == TicketReferenceValidationResult.InvalidConfiguration ? "Configuration" : "Validation" }, "Current ticket-reference policy does not permit execution.", cancellationToken);
            return new(MembershipRequestStatus.Rejected, "Die Ticket-Referenz erfüllt die aktuelle Richtlinie nicht mehr.");
        }
        var authorization = await authorize.ExecuteAsync(new(command.Actor, command.TargetAccountId, command.TargetGroupId, command.RequestedTtlSeconds), cancellationToken);
        // From Created, a policy requiring a second factor must not be executed directly - it must first go through
        // AwaitingSecondFactor/SecondFactorValidated. From SecondFactorValidated, that factor is already satisfied.
        if (authorization is null || (fromStatus == MembershipRequestStatus.Created && authorization.RequiresSecondFactor))
        {
            await stateStore.TransitionAsync(command.RequestId, fromStatus, MembershipRequestStatus.Rejected, command.AuditContext with { FailureCategory = "Authorization" }, "Current authorization or MFA policy does not permit execution.", cancellationToken);
            return new(MembershipRequestStatus.Rejected, "Die Berechtigung oder aktuelle Richtlinie erlaubt die Ausführung nicht mehr.");
        }

        if (fromStatus == MembershipRequestStatus.AwaitingApproval)
        {
            // Approval already granted in an earlier pass through this method - resume directly into the queue.
            await stateStore.TransitionAsync(command.RequestId, MembershipRequestStatus.AwaitingApproval, MembershipRequestStatus.Queued, command.AuditContext, "Worker command queued after approval.", cancellationToken);
        }
        else if (authorization.RequiresApproval)
        {
            await stateStore.TransitionAsync(command.RequestId, fromStatus, MembershipRequestStatus.Validated, command.AuditContext, "Current authorization succeeded.", cancellationToken);
            await stateStore.TransitionAsync(command.RequestId, MembershipRequestStatus.Validated, MembershipRequestStatus.AwaitingApproval, command.AuditContext, "Policy requires approval before execution.", cancellationToken);
            return new(MembershipRequestStatus.AwaitingApproval, "Die Anfrage wartet auf eine Genehmigung.");
        }
        else
        {
            await stateStore.TransitionAsync(command.RequestId, fromStatus, MembershipRequestStatus.Validated, command.AuditContext, "Current authorization succeeded.", cancellationToken);
            await stateStore.TransitionAsync(command.RequestId, MembershipRequestStatus.Validated, MembershipRequestStatus.Queued, command.AuditContext, "Worker command queued.", cancellationToken);
        }
        await stateStore.TransitionAsync(command.RequestId, MembershipRequestStatus.Queued, MembershipRequestStatus.Executing, command.AuditContext, "Worker command dispatched.", cancellationToken);

        var result = await workerClient.DispatchAsync(new(command.RequestId, command.CorrelationId, authorization.DirectoryScopeId, authorization.TargetAccountObjectGuid, authorization.TargetGroupObjectGuid, command.RequestedTtlSeconds), cancellationToken);
        if (result.Kind != WorkerMembershipDispatchKind.Completed || result.MembershipResult?.Kind != TemporaryGroupMembershipResultKind.Verified)
        {
            await stateStore.TransitionAsync(command.RequestId, MembershipRequestStatus.Executing, MembershipRequestStatus.Failed, command.AuditContext with { FailureCategory = "ActiveDirectory" }, result.FailureCode ?? result.MembershipResult?.ErrorCode ?? "Worker execution or verification failed.", cancellationToken);
            return new(MembershipRequestStatus.Failed, MembershipRequestUserMessages.ForWorkerResult(result));
        }

        await stateStore.TransitionAsync(command.RequestId, MembershipRequestStatus.Executing, MembershipRequestStatus.VerificationPending, command.AuditContext, "Worker returned a verified AD TTL membership.", cancellationToken);
        await stateStore.TransitionAsync(command.RequestId, MembershipRequestStatus.VerificationPending, MembershipRequestStatus.Succeeded, command.AuditContext, "AD TTL membership read-back verified by Worker.", cancellationToken);
        return new(MembershipRequestStatus.Succeeded, "Die zeitlich begrenzte Gruppenmitgliedschaft wurde in Active Directory bestätigt.");
    }
}
