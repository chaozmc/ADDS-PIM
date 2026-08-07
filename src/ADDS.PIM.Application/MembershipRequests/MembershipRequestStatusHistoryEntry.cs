using ADDS.PIM.Domain.MembershipRequests;

namespace ADDS.PIM.Application.MembershipRequests;

public sealed record MembershipRequestStatusHistoryEntry(
    Guid EntryId,
    Guid RequestId,
    MembershipRequestStatus? PreviousStatus,
    MembershipRequestStatus NewStatus,
    DateTimeOffset OccurredUtc,
    string ActorId,
    string SourceComponent,
    string Reason);
