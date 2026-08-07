using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ADDS.PIM.Application.Security;

namespace ADDS.PIM.Application.Tests.Security;

public sealed class ApiRequestCanonicalizerTests
{
    [Fact]
    public void ComputeSignature_BindsEveryRequestFieldAndRejectsTampering()
    {
        using var certificate = CreateSigningCertificate();
        var unsigned = CreateSignature();
        var signed = unsigned with { Signature = ApiRequestCanonicalizer.ComputeSignature(unsigned, certificate) };

        Assert.True(ApiRequestCanonicalizer.HasValidSignature(signed, certificate));
        Assert.False(ApiRequestCanonicalizer.HasValidSignature(signed with { Path = "/api/v1/other" }, certificate));
        Assert.False(ApiRequestCanonicalizer.HasValidSignature(signed with { RequestId = Guid.NewGuid() }, certificate));
        Assert.False(ApiRequestCanonicalizer.HasValidSignature(signed with { BodyHash = ApiRequestCanonicalizer.ComputeBodyHash("changed"u8) }, certificate));
    }

    [Fact]
    public void CanonicalizeQuery_SortsAndPercentEncodesDeterministically()
    {
        var query = ApiRequestCanonicalizer.CanonicalizeQuery(
        [
            new("z", "two words"),
            new("a", "ä"),
            new("a", "1")
        ]);

        Assert.Equal("a=1&a=%C3%A4&z=two%20words", query);
    }

    [Fact]
    public void HasValidSignature_RejectsMalformedBase64UrlSignature()
    {
        using var certificate = CreateSigningCertificate();
        var signed = CreateSignature() with { Signature = "not+base64url" };

        Assert.False(ApiRequestCanonicalizer.HasValidSignature(signed, certificate));
    }

    private static ApiRequestSignature CreateSignature() => new(
        ApiRequestSignature.CurrentVersion,
        "web-primary",
        Guid.NewGuid(),
        Guid.NewGuid(),
        new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero),
        "a_valid_nonce",
        "POST",
        "/api/v1/membership-requests",
        "a=1&z=2",
        ApiRequestCanonicalizer.ComputeBodyHash("{}"u8),
        "application/json",
        "v1",
        string.Empty);

    private static X509Certificate2 CreateSigningCertificate()
    {
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest("CN=ADDS-PIM Web Signing", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
