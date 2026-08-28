namespace ADDS.PIM.Application.Security;

/// <summary>Protects an arbitrary secret with a certificate's RSA key pair, without exposing key material to
/// application workflows. Used for TOTP secrets and for the SMTP password in mail settings - any secret
/// protected by the same configured certificate shares one <see cref="KeyId"/> namespace, so a single
/// certificate rollover re-encrypts all of them together.</summary>
public interface ICertificateSecretProtector
{
    string KeyId { get; }

    byte[] Protect(ReadOnlySpan<byte> secret);

    byte[] Unprotect(ReadOnlySpan<byte> protectedSecret, string keyId);
}
