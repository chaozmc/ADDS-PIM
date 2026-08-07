using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ADDS.PIM.Application.Security;

namespace ADDS.PIM.Application.Tests.Security;

public sealed class ValidateApiRequestSignatureUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ValidCurrentSignatureRegistersReplayProtection()
    {
        var now = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero); using var certificate = CreateCertificate();
        var unsigned = Create(now); var signature = unsigned with { Signature = ApiRequestCanonicalizer.ComputeSignature(unsigned, certificate) };
        var store = new FakeStore(new(ApiRequestReplayRegistrationKind.Accepted));
        var result = await new ValidateApiRequestSignatureUseCase(new FakeCertificates(certificate), store, new FixedTime(now)).ExecuteAsync(signature, CancellationToken.None);
        Assert.Equal(ApiRequestReplayRegistrationKind.Accepted, result.Kind); Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_ExpiredOrTamperedRequestDoesNotReachReplayStore()
    {
        var now = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero); using var certificate = CreateCertificate();
        var unsigned = Create(now.AddMinutes(-6)); var signature = unsigned with { Signature = ApiRequestCanonicalizer.ComputeSignature(unsigned, certificate) };
        var store = new FakeStore(new(ApiRequestReplayRegistrationKind.Accepted));
        var result = await new ValidateApiRequestSignatureUseCase(new FakeCertificates(certificate), store, new FixedTime(now)).ExecuteAsync(signature, CancellationToken.None);
        Assert.Equal(ApiRequestReplayRegistrationKind.Conflict, result.Kind); Assert.Equal(0, store.Calls);
    }

    private static ApiRequestSignature Create(DateTimeOffset issued) => new(ApiRequestSignature.CurrentVersion, "web-primary", Guid.NewGuid(), Guid.NewGuid(), issued, "nonce_value", "POST", "/api/v1/membership-requests", "", ApiRequestCanonicalizer.ComputeBodyHash("{}"u8), "application/json", "v1", "");
    private static X509Certificate2 CreateCertificate() { using var rsa = RSA.Create(3072); var request = new CertificateRequest("CN=Web", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pss); request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true)); return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)); }
    private sealed class FakeCertificates(X509Certificate2 certificate) : IWebSigningCertificateResolver { public Task<X509Certificate2?> ResolveActiveCertificateAsync(string keyId, CancellationToken cancellationToken) => Task.FromResult<X509Certificate2?>(certificate); }
    private sealed class FakeStore(ApiRequestReplayRegistration result) : IApiRequestReplayStore { public int Calls { get; private set; } public Task<ApiRequestReplayRegistration> RegisterAsync(ApiRequestSignature signature, string canonicalRequestHash, DateTimeOffset receivedUtc, CancellationToken cancellationToken) { Calls++; return Task.FromResult(result); } }
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
