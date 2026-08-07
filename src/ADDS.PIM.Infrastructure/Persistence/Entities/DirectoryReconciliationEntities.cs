using ADDS.PIM.Application.Administration;

namespace ADDS.PIM.Infrastructure.Persistence.Entities;

public sealed class DirectoryReconciliationRunEntity
{
    public Guid RunId { get; set; }
    public Guid DirectoryScopeId { get; set; }
    public Guid RequestedByObjectGuid { get; set; }
    public Guid CorrelationId { get; set; }
    public required string FrontendClientId { get; set; }
    public string? SourceIpAddress { get; set; }
    public DirectoryReconciliationRunStatus Status { get; set; }
    public DateTimeOffset RequestedUtc { get; set; }
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public string? FailureCategory { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class DirectoryReconciliationFindingEntity
{
    public Guid FindingId { get; set; }
    public Guid RunId { get; set; }
    public DirectoryReconciliationEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public Guid DirectoryScopeId { get; set; }
    public Guid ObjectGuid { get; set; }
    public DirectoryReconciliationFindingReason Reason { get; set; }
    public required string DisplayNameSnapshot { get; set; }
    public DateTimeOffset DetectedUtc { get; set; }
    public bool IsResolved { get; set; }
    public DateTimeOffset? ResolvedUtc { get; set; }
    public DateTimeOffset? DeactivatedUtc { get; set; }
    public Guid? DeactivatedByObjectGuid { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
