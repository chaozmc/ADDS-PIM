namespace ADDS.PIM.Infrastructure.Mfa;

/// <summary>Non-secret selector for the API certificate that protects TOTP secrets.</summary>
public sealed class TotpSecretProtectionOptions
{
    public const string SectionName = "TotpSecretProtection";

    public string? CertificateThumbprint { get; init; }
}
