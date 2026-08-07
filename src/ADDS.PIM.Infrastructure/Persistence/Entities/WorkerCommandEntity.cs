using ADDS.PIM.Application.Worker;

namespace ADDS.PIM.Infrastructure.Persistence.Entities;

public sealed class WorkerCommandEntity
{
    public Guid CommandId { get; set; }
    public Guid RequestId { get; set; }
    public Guid CorrelationId { get; set; }
    public required string Nonce { get; set; }
    public required string CommandHash { get; set; }
    public required string CallerCertificateThumbprint { get; set; }
    public Guid DirectoryScopeId { get; set; }
    public Guid TargetAccountObjectGuid { get; set; }
    public Guid TargetGroupObjectGuid { get; set; }
    public long RequestedTtlSeconds { get; set; }
    public DateTimeOffset IssuedUtc { get; set; }
    public DateTimeOffset ReceivedUtc { get; set; }
    public WorkerCommandStatus Status { get; set; }
    public string? ResultKind { get; set; }
    public string? ResultDomainController { get; set; }
    public long? ResultRemainingTtlSeconds { get; set; }
    public string? ResultErrorCode { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
