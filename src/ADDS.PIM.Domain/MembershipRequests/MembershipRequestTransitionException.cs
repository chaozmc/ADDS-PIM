namespace ADDS.PIM.Domain.MembershipRequests;

public sealed class MembershipRequestTransitionException(
    MembershipRequestStatus currentStatus,
    MembershipRequestStatus requestedStatus) : InvalidOperationException(
        $"The transition from '{currentStatus}' to '{requestedStatus}' is not allowed.")
{
    public MembershipRequestStatus CurrentStatus { get; } = currentStatus;

    public MembershipRequestStatus RequestedStatus { get; } = requestedStatus;
}
