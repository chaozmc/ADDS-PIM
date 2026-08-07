using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Application.Mfa;
using ADDS.PIM.Domain.Security;

namespace ADDS.PIM.Application.Tests.Mfa;

public sealed class SecondFactorEligibilityValidatorTests
{
    [Fact]
    public async Task HasEligibleFactorAsync_ReturnsFalse_WhenTotpAllowedButNoActiveFactorExists()
    {
        var store = new FakeTotpVerificationStore(activeFactor: null);
        var validator = new SecondFactorEligibilityValidator(store, new FakeFido2CredentialStore());

        var result = await validator.HasEligibleFactorAsync(Guid.NewGuid(), SecondFactorType.Totp, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task HasEligibleFactorAsync_ReturnsTrue_WhenTotpAllowedAndActiveFactorExists()
    {
        var factor = new ActiveTotpFactor(Guid.NewGuid(), Guid.NewGuid(), [1, 2, 3], "key-1", null, null);
        var store = new FakeTotpVerificationStore(factor);
        var validator = new SecondFactorEligibilityValidator(store, new FakeFido2CredentialStore());

        var result = await validator.HasEligibleFactorAsync(factor.PersonId, SecondFactorType.Totp, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task HasEligibleFactorAsync_ReturnsTrue_EvenWhenTheActiveFactorIsCurrentlyLocked()
    {
        var factor = new ActiveTotpFactor(Guid.NewGuid(), Guid.NewGuid(), [1, 2, 3], "key-1", 42, DateTimeOffset.UtcNow.AddMinutes(10));
        var store = new FakeTotpVerificationStore(factor);
        var validator = new SecondFactorEligibilityValidator(store, new FakeFido2CredentialStore());

        var result = await validator.HasEligibleFactorAsync(factor.PersonId, SecondFactorType.Totp, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task HasEligibleFactorAsync_ReturnsFalse_WhenTotpIsNotAmongTheAllowedFactorTypes()
    {
        var factor = new ActiveTotpFactor(Guid.NewGuid(), Guid.NewGuid(), [1, 2, 3], "key-1", null, null);
        var store = new FakeTotpVerificationStore(factor);
        var validator = new SecondFactorEligibilityValidator(store, new FakeFido2CredentialStore());

        var result = await validator.HasEligibleFactorAsync(factor.PersonId, SecondFactorType.None, CancellationToken.None);

        Assert.False(result);
    }

    private sealed class FakeTotpVerificationStore(ActiveTotpFactor? activeFactor) : ITotpVerificationStore
    {
        public Task<ActiveTotpFactor?> FindActiveAsync(Guid personId, CancellationToken cancellationToken)
            => Task.FromResult(activeFactor);

        public Task RecordSuccessAsync(Guid totpFactorId, Guid personId, Guid mfaTransactionId, Guid requestId, long timeStep, DateTimeOffset usedUtc, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<TotpFailureOutcome> RecordFailureAsync(Guid totpFactorId, Guid personId, Guid mfaTransactionId, Guid requestId, DateTimeOffset failedUtc, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken)
            => Task.FromResult(new TotpFailureOutcome(1, null));

        public Task RecordRejectionAsync(Guid personId, Guid requestId, string eventType, DateTimeOffset occurredUtc, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RecordStepUpSuccessAsync(Guid totpFactorId, Guid personId, long timeStep, DateTimeOffset usedUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<TotpFailureOutcome> RecordStepUpFailureAsync(Guid totpFactorId, Guid personId, DateTimeOffset failedUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken)
            => Task.FromResult(new TotpFailureOutcome(1, null));
    }

    private sealed class FakeFido2CredentialStore(int activeCount = 0) : IFido2CredentialStore
    {
        public Task<int> CountActiveAsync(Guid personId, CancellationToken cancellationToken) => Task.FromResult(activeCount);
        public Task<IReadOnlyList<ActiveFido2Credential>> ListActiveAsync(Guid personId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ActiveFido2Credential>>([]);
        public Task CreateAsync(NewStoredFido2Credential credential, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateSignatureCounterAsync(Guid fido2CredentialId, long signatureCounter, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RecordStepUpSuccessAsync(Guid fido2CredentialId, Guid personId, long newSignatureCounter, DateTimeOffset occurredUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RecordStepUpFailureAsync(Guid personId, DateTimeOffset occurredUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
