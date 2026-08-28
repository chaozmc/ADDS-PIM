using ADDS.PIM.Application.Mfa;
using ADDS.PIM.Application.Security;

namespace ADDS.PIM.Application.Tests.Mfa;

public sealed class ConfirmTotpEnrollmentUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ConfirmsOnlyAValidUnexpiredCode()
    {
        var now = DateTimeOffset.UtcNow;
        var secret = Enumerable.Range(0, 20).Select(x => (byte)x).ToArray();
        var store = new FakeStore(new PendingTotpEnrollment(Guid.NewGuid(), Guid.NewGuid(), secret, "key", now.AddMinutes(1)));
        var code = Totp.Generate(secret, now);
        var command = new ConfirmTotpEnrollmentCommand(store.Factor.PersonId, store.Factor.TotpFactorId, code, Guid.NewGuid(), "test", null, null);
        var result = await new ConfirmTotpEnrollmentUseCase(new Protector(), store, new FixedTime(now)).ExecuteAsync(command, CancellationToken.None);
        Assert.True(result); Assert.True(store.Confirmed);
    }
    private sealed class Protector : ICertificateSecretProtector { public string KeyId => "key"; public byte[] Protect(ReadOnlySpan<byte> value) => value.ToArray(); public byte[] Unprotect(ReadOnlySpan<byte> value, string keyId) => value.ToArray(); }
    private sealed class FakeStore(PendingTotpEnrollment factor) : ITotpEnrollmentConfirmationStore
    {
        public PendingTotpEnrollment Factor { get; } = factor;
        public bool Confirmed { get; private set; }
        public Task<PendingTotpEnrollment?> FindPendingAsync(Guid personId, Guid factorId, CancellationToken ct) => Task.FromResult<PendingTotpEnrollment?>(Factor);
        public Task<bool> ConfirmAsync(Guid personId, Guid factorId, DateTimeOffset confirmedUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken ct) { Confirmed = true; return Task.FromResult(true); }
        public Task RecordRejectionAsync(Guid personId, Guid factorId, string eventType, DateTimeOffset occurredUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
