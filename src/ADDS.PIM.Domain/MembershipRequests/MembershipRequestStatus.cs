namespace ADDS.PIM.Domain.MembershipRequests;

public enum MembershipRequestStatus
{
    Created,
    AwaitingSecondFactor,
    SecondFactorValidated,
    Validated,
    Queued,
    Executing,
    VerificationPending,
    Succeeded,
    Rejected,
    Failed,
    Expired,
    Cancelled,
    AwaitingApproval
}
