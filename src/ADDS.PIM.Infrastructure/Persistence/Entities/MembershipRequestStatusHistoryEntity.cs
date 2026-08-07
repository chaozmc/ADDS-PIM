using ADDS.PIM.Domain.MembershipRequests;

namespace ADDS.PIM.Infrastructure.Persistence.Entities;

public sealed class MembershipRequestStatusHistoryEntity
{
    public Guid EntryId { get; set; }

    public Guid RequestId { get; set; }

    public MembershipRequestStatus? PreviousStatus { get; set; }

    public MembershipRequestStatus NewStatus { get; set; }

    public DateTimeOffset OccurredUtc { get; set; }

    public required string ActorId { get; set; }

    public required string SourceComponent { get; set; }

    public required string Reason { get; set; }
}
