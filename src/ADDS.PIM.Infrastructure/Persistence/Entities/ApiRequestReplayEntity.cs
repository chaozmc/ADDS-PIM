namespace ADDS.PIM.Infrastructure.Persistence.Entities;

public sealed class ApiRequestReplayEntity
{
    public Guid ReplayId { get; set; }
    public required string KeyId { get; set; }
    public Guid RequestId { get; set; }
    public required string Nonce { get; set; }
    public required string CanonicalRequestHash { get; set; }
    public DateTimeOffset IssuedUtc { get; set; }
    public DateTimeOffset ReceivedUtc { get; set; }
}
