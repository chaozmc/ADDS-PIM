namespace ADDS.PIM.Application.Mfa;

/// <summary>Protects a TOTP secret without exposing key material to application workflows.</summary>
public interface ITotpSecretProtector
{
    string KeyId { get; }

    byte[] Protect(ReadOnlySpan<byte> secret);

    byte[] Unprotect(ReadOnlySpan<byte> protectedSecret, string keyId);
}
