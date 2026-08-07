namespace ADDS.PIM.Domain.MembershipRequests;

/// <summary>
/// Central policy for legal membership-request transitions.
/// </summary>
public static class MembershipRequestStateMachine
{
    public static bool CanTransition(
        MembershipRequestStatus currentStatus,
        MembershipRequestStatus requestedStatus) =>
        currentStatus != requestedStatus
        && !IsTerminal(currentStatus)
        && (IsEarlyTerminalOutcome(requestedStatus) || IsNormalTransition(currentStatus, requestedStatus));

    public static bool IsTerminal(MembershipRequestStatus status) =>
        status is MembershipRequestStatus.Succeeded
            or MembershipRequestStatus.Rejected
            or MembershipRequestStatus.Failed
            or MembershipRequestStatus.Expired
            or MembershipRequestStatus.Cancelled;

    private static bool IsEarlyTerminalOutcome(MembershipRequestStatus status) =>
        status is MembershipRequestStatus.Rejected
            or MembershipRequestStatus.Failed
            or MembershipRequestStatus.Expired
            or MembershipRequestStatus.Cancelled;

    private static bool IsNormalTransition(
        MembershipRequestStatus currentStatus,
        MembershipRequestStatus requestedStatus) =>
        (currentStatus, requestedStatus) switch
        {
            (MembershipRequestStatus.Created, MembershipRequestStatus.AwaitingSecondFactor) => true,
            (MembershipRequestStatus.Created, MembershipRequestStatus.Validated) => true,
            (MembershipRequestStatus.AwaitingSecondFactor, MembershipRequestStatus.SecondFactorValidated) => true,
            (MembershipRequestStatus.SecondFactorValidated, MembershipRequestStatus.Validated) => true,
            (MembershipRequestStatus.Validated, MembershipRequestStatus.AwaitingApproval) => true,
            (MembershipRequestStatus.AwaitingApproval, MembershipRequestStatus.Queued) => true,
            (MembershipRequestStatus.Validated, MembershipRequestStatus.Queued) => true,
            (MembershipRequestStatus.Queued, MembershipRequestStatus.Executing) => true,
            (MembershipRequestStatus.Executing, MembershipRequestStatus.VerificationPending) => true,
            (MembershipRequestStatus.VerificationPending, MembershipRequestStatus.Succeeded) => true,
            _ => false
        };
}
