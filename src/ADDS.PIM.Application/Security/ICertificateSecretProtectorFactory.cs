namespace ADDS.PIM.Application.Security;

/// <summary>Constructs an <see cref="ICertificateSecretProtector"/> for an arbitrary certificate thumbprint, independent
/// of the DI singleton's configured thumbprint. Used only by the certificate rotation administrative workflow
/// to build the outgoing (currently configured) and incoming (new) protectors for a re-encryption
/// pass; normal enrollment/verification/mail-settings flows keep using the singleton <see cref="ICertificateSecretProtector"/>.</summary>
public interface ICertificateSecretProtectorFactory
{
    /// <summary>Throws if the certificate cannot be found in LocalMachine\My, or is expired, missing KeyEncipherment usage, or its private key is inaccessible.</summary>
    ICertificateSecretProtector CreateForThumbprint(string thumbprint);
}
