using ADDS.PIM.Application.Mfa;

namespace ADDS.PIM.Application.Tests.Mfa;

public sealed class CreateTotpEnrollmentUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_GeneratesAndEncryptsAnAtLeast160BitSecret()
    {
        var protector = new FakeProtector(); var store = new FakeStore(hasActiveFactor: false);
        var result = await new CreateTotpEnrollmentUseCase(protector, store, TimeProvider.System).ExecuteAsync(new CreateTotpEnrollmentCommand(Guid.NewGuid(), Guid.NewGuid(), "test", null, null), CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(20, result.Secret.Length); Assert.Single(store.Created);
        Assert.Equal(result.TotpFactorId, store.Created[0].TotpFactorId);
        Assert.False(result.Secret.AsSpan().SequenceEqual(store.Created[0].EncryptedSecret));
        Assert.Equal("test-key", store.Created[0].ProtectionKeyId);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsWhenAnActiveFactorAlreadyExists()
    {
        var store = new FakeStore(hasActiveFactor: true);
        var result = await new CreateTotpEnrollmentUseCase(new FakeProtector(), store, TimeProvider.System).ExecuteAsync(new CreateTotpEnrollmentCommand(Guid.NewGuid(), Guid.NewGuid(), "test", null, null), CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(store.Created);
    }

    private sealed class FakeProtector : ITotpSecretProtector
    {
        public string KeyId => "test-key";
        public byte[] Protect(ReadOnlySpan<byte> secret) => secret.ToArray().Reverse().ToArray();
        public byte[] Unprotect(ReadOnlySpan<byte> protectedSecret, string keyId) => protectedSecret.ToArray().Reverse().ToArray();
    }
    private sealed class FakeStore(bool hasActiveFactor) : ITotpFactorEnrollmentStore
    {
        public List<NewTotpFactor> Created { get; } = [];
        public Task<bool> HasActiveFactorAsync(Guid personId, CancellationToken cancellationToken) => Task.FromResult(hasActiveFactor);
        public Task CreateAsync(NewTotpFactor factor, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken) { Created.Add(factor); return Task.CompletedTask; }
    }
}
