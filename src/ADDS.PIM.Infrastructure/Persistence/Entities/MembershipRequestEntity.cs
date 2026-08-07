using ADDS.PIM.Domain.MembershipRequests;

namespace ADDS.PIM.Infrastructure.Persistence.Entities;

public sealed class MembershipRequestEntity
{
    public Guid RequestId { get; set; }

    public Guid PersonId { get; set; }

    public Guid ActorAccountId { get; set; }

    public Guid TargetAccountId { get; set; }

    public Guid TargetGroupId { get; set; }

    public Guid EntitlementId { get; set; }

    public required string PersonDisplayNameSnapshot { get; set; }

    public required string ActorAccountDisplayNameSnapshot { get; set; }

    public required string TargetAccountDisplayNameSnapshot { get; set; }

    public required string TargetGroupDisplayNameSnapshot { get; set; }

    public long RequestedTtlSeconds { get; set; }

    public required string Reason { get; set; }

    public string? TicketReference { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public MembershipRequestStatus Status { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
