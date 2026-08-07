namespace ADDS.PIM.Infrastructure.Persistence.Entities;

public sealed class WebSigningCertificateEntity
{
    public Guid WebSigningCertificateId { get; set; }
    public required string KeyId { get; set; }
    public required string Thumbprint { get; set; }
    public required byte[] PublicCertificateDer { get; set; }
    public required string Purpose { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset ValidFromUtc { get; set; }
    public DateTimeOffset? ValidUntilUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public required string CreatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
