using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Application.Mfa;
using ADDS.PIM.Domain.MembershipRequests;
using ADDS.PIM.Domain.Security;

namespace ADDS.PIM.Application.Tests.Mfa;

public sealed class VerifyTotpTransactionUseCaseTests
{
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid PersonId = Guid.NewGuid();
    private static readonly byte[] Secret = Enumerable.Range(0, 20).Select(x => (byte)x).ToArray();
    private static readonly MembershipRequestTransitionAuditContext AuditContext = new(Guid.NewGuid(), "Web", "Windows", "requires-totp", "127.0.0.1");

    [Fact]
    public async Task ExecuteAsync_ValidCodeSucceedsAndTransitionsToSecondFactorValidated()
    {
        var now = DateTimeOffset.UtcNow;
        var transactionStore = new FakeTransactionStore(PendingTransaction(now));
        var totpStore = new FakeTotpVerificationStore(ActiveFactor());
        var stateStore = new FakeStateStore();
        var useCase = NewUseCase(transactionStore, totpStore, stateStore, now);

        var result = await useCase.ExecuteAsync(new(RequestId, PersonId, Totp.Generate(Secret, now), AuditContext), CancellationToken.None);

        Assert.Equal(TotpTransactionVerificationOutcome.Succeeded, result.Outcome);
        Assert.True(transactionStore.Consumed);
        Assert.Contains(stateStore.Transitions, t => t is (MembershipRequestStatus.AwaitingSecondFactor, MembershipRequestStatus.SecondFactorValidated));
    }

    [Fact]
    public async Task ExecuteAsync_ReplayOfAnAlreadyUsedTimeStepFails()
    {
        var now = DateTimeOffset.UtcNow;
        var currentStep = now.ToUnixTimeSeconds() / 30;
        var transactionStore = new FakeTransactionStore(PendingTransaction(now));
        var totpStore = new FakeTotpVerificationStore(ActiveFactor() with { LastUsedTimeStep = currentStep });
        var useCase = NewUseCase(transactionStore, totpStore, new FakeStateStore(), now);

        var result = await useCase.ExecuteAsync(new(RequestId, PersonId, Totp.Generate(Secret, now), AuditContext), CancellationToken.None);

        Assert.Equal(TotpTransactionVerificationOutcome.InvalidCode, result.Outcome);
        Assert.False(transactionStore.Consumed);
    }

    [Fact]
    public async Task ExecuteAsync_ExpiredTransactionFailsAndTransitionsRequestToExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var transactionStore = new FakeTransactionStore(PendingTransaction(now) with { ExpiresUtc = now.AddMinutes(-1) });
        var stateStore = new FakeStateStore();
        var useCase = NewUseCase(transactionStore, new FakeTotpVerificationStore(ActiveFactor()), stateStore, now);

        var result = await useCase.ExecuteAsync(new(RequestId, PersonId, Totp.Generate(Secret, now), AuditContext), CancellationToken.None);

        Assert.Equal(TotpTransactionVerificationOutcome.TransactionExpired, result.Outcome);
        Assert.Contains(stateStore.Transitions, t => t is (MembershipRequestStatus.AwaitingSecondFactor, MembershipRequestStatus.Expired));
    }

    [Fact]
    public async Task ExecuteAsync_WrongTransactionOrPersonFails()
    {
        var now = DateTimeOffset.UtcNow;
        var transactionStore = new FakeTransactionStore(null);
        var useCase = NewUseCase(transactionStore, new FakeTotpVerificationStore(ActiveFactor()), new FakeStateStore(), now);

        var result = await useCase.ExecuteAsync(new(RequestId, PersonId, Totp.Generate(Secret, now), AuditContext), CancellationToken.None);

        Assert.Equal(TotpTransactionVerificationOutcome.TransactionNotFound, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_LocksAfterFiveConsecutiveFailuresAndRejectsSubsequentAttemptWithoutEvaluatingCode()
    {
        var now = DateTimeOffset.UtcNow;
        var transactionStore = new FakeTransactionStore(PendingTransaction(now));
        var totpStore = new FakeTotpVerificationStore(ActiveFactor());
        var useCase = NewUseCase(transactionStore, totpStore, new FakeStateStore(), now);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var result = await useCase.ExecuteAsync(new(RequestId, PersonId, "000000", AuditContext), CancellationToken.None);
            Assert.True(result.Outcome is TotpTransactionVerificationOutcome.InvalidCode or TotpTransactionVerificationOutcome.Locked);
        }

        Assert.NotNull(totpStore.LockedUntilUtc);
        var recordCallsBeforeLockedAttempt = totpStore.RecordCalls;
        var lockedResult = await useCase.ExecuteAsync(new(RequestId, PersonId, Totp.Generate(Secret, now), AuditContext), CancellationToken.None);

        Assert.Equal(TotpTransactionVerificationOutcome.Locked, lockedResult.Outcome);
        Assert.Equal(recordCallsBeforeLockedAttempt, totpStore.RecordCalls);
    }

    [Fact]
    public async Task ExecuteAsync_WindowBoundaryAcceptsOneAdjacentStepAndRejectsTwo()
    {
        var now = DateTimeOffset.UtcNow;
        var oneStepAgo = Totp.Generate(Secret, now.AddSeconds(-30));
        var twoStepsAgo = Totp.Generate(Secret, now.AddSeconds(-60));

        var acceptResult = await NewUseCase(new FakeTransactionStore(PendingTransaction(now)), new FakeTotpVerificationStore(ActiveFactor()), new FakeStateStore(), now)
            .ExecuteAsync(new(RequestId, PersonId, oneStepAgo, AuditContext), CancellationToken.None);
        Assert.Equal(TotpTransactionVerificationOutcome.Succeeded, acceptResult.Outcome);

        var rejectResult = await NewUseCase(new FakeTransactionStore(PendingTransaction(now)), new FakeTotpVerificationStore(ActiveFactor()), new FakeStateStore(), now)
            .ExecuteAsync(new(RequestId, PersonId, twoStepsAgo, AuditContext), CancellationToken.None);
        Assert.Equal(TotpTransactionVerificationOutcome.InvalidCode, rejectResult.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_CrossAccountSubstitutionFails()
    {
        var now = DateTimeOffset.UtcNow;
        var otherPersonSecret = Enumerable.Range(20, 20).Select(x => (byte)x).ToArray();
        var transactionStore = new FakeTransactionStore(PendingTransaction(now));
        var totpStore = new FakeTotpVerificationStore(ActiveFactor());
        var useCase = NewUseCase(transactionStore, totpStore, new FakeStateStore(), now);

        var result = await useCase.ExecuteAsync(new(RequestId, PersonId, Totp.Generate(otherPersonSecret, now), AuditContext), CancellationToken.None);

        Assert.Equal(TotpTransactionVerificationOutcome.InvalidCode, result.Outcome);
        Assert.False(transactionStore.Consumed);
    }

    [Fact]
    public async Task ExecuteAsync_NoActiveFactorFails()
    {
        var now = DateTimeOffset.UtcNow;
        var totpStore = new FakeTotpVerificationStore(null);
        var useCase = NewUseCase(new FakeTransactionStore(PendingTransaction(now)), totpStore, new FakeStateStore(), now);

        var result = await useCase.ExecuteAsync(new(RequestId, PersonId, "123456", AuditContext), CancellationToken.None);

        Assert.Equal(TotpTransactionVerificationOutcome.NoActiveFactor, result.Outcome);
        Assert.Contains("TotpVerificationRejectedNoActiveFactor", totpStore.Rejections);
    }

    [Fact]
    public async Task ExecuteAsync_LockedFactorRejectsWithoutCallingRecordFailure()
    {
        var now = DateTimeOffset.UtcNow;
        var totpStore = new FakeTotpVerificationStore(ActiveFactor() with { LockedUntilUtc = now.AddMinutes(5) });
        var useCase = NewUseCase(new FakeTransactionStore(PendingTransaction(now)), totpStore, new FakeStateStore(), now);

        var result = await useCase.ExecuteAsync(new(RequestId, PersonId, Totp.Generate(Secret, now), AuditContext), CancellationToken.None);

        Assert.Equal(TotpTransactionVerificationOutcome.Locked, result.Outcome);
        Assert.Equal(0, totpStore.RecordCalls);
        Assert.Contains("TotpVerificationRejectedLocked", totpStore.Rejections);
    }

    [Fact]
    public async Task ExecuteAsync_FactorNotAllowedByPolicyFails()
    {
        var now = DateTimeOffset.UtcNow;
        var transactionStore = new FakeTransactionStore(PendingTransaction(now) with { AllowedFactorTypes = SecondFactorType.Fido2 });
        var totpStore = new FakeTotpVerificationStore(ActiveFactor());
        var useCase = NewUseCase(transactionStore, totpStore, new FakeStateStore(), now);

        var result = await useCase.ExecuteAsync(new(RequestId, PersonId, Totp.Generate(Secret, now), AuditContext), CancellationToken.None);

        Assert.Equal(TotpTransactionVerificationOutcome.FactorNotAllowed, result.Outcome);
        Assert.Contains("TotpVerificationRejectedFactorNotAllowed", totpStore.Rejections);
    }

    private static PendingMfaTransaction PendingTransaction(DateTimeOffset now)
        => new(Guid.NewGuid(), RequestId, PersonId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3600, SecondFactorType.Totp, now.AddMinutes(5));

    private static ActiveTotpFactor ActiveFactor()
        => new(Guid.NewGuid(), PersonId, Secret, "key", null, null);

    private static VerifyTotpTransactionUseCase NewUseCase(IMfaTransactionStore transactionStore, ITotpVerificationStore totpStore, IMembershipRequestStateStore stateStore, DateTimeOffset now)
        => new(transactionStore, totpStore, new PassthroughProtector(), stateStore, new FixedTime(now));

    private sealed class PassthroughProtector : ITotpSecretProtector
    {
        public string KeyId => "key";
        public byte[] Protect(ReadOnlySpan<byte> secret) => secret.ToArray();
        public byte[] Unprotect(ReadOnlySpan<byte> protectedSecret, string keyId) => protectedSecret.ToArray();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeTransactionStore(PendingMfaTransaction? transaction) : IMfaTransactionStore
    {
        public bool Consumed { get; private set; }
        public Task<PendingMfaTransaction?> FindPendingAsync(Guid requestId, Guid personId, CancellationToken cancellationToken)
            => Task.FromResult(transaction is not null && transaction.RequestId == requestId && transaction.PersonId == personId ? transaction : null);
        public Task<bool> TryConsumeAsync(Guid mfaTransactionId, SecondFactorType satisfiedBy, DateTimeOffset consumedUtc, CancellationToken cancellationToken)
        {
            Consumed = true;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeTotpVerificationStore(ActiveTotpFactor? factor) : ITotpVerificationStore
    {
        private int failures;
        public DateTimeOffset? LockedUntilUtc { get; private set; } = factor?.LockedUntilUtc;
        public int RecordCalls { get; private set; }

        public Task<ActiveTotpFactor?> FindActiveAsync(Guid personId, CancellationToken cancellationToken)
            => Task.FromResult(factor is null ? null : factor with { LockedUntilUtc = LockedUntilUtc });

        public Task RecordSuccessAsync(Guid totpFactorId, Guid personId, Guid mfaTransactionId, Guid requestId, long timeStep, DateTimeOffset usedUtc, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken)
        {
            RecordCalls++;
            failures = 0;
            factor = factor! with { LastUsedTimeStep = timeStep };
            return Task.CompletedTask;
        }

        public Task<TotpFailureOutcome> RecordFailureAsync(Guid totpFactorId, Guid personId, Guid mfaTransactionId, Guid requestId, DateTimeOffset failedUtc, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken)
        {
            RecordCalls++;
            failures++;
            if (failures >= 5)
            {
                LockedUntilUtc = failedUtc.AddMinutes(15);
            }
            return Task.FromResult(new TotpFailureOutcome(failures, LockedUntilUtc));
        }

        public List<string> Rejections { get; } = [];
        public Task RecordRejectionAsync(Guid personId, Guid requestId, string eventType, DateTimeOffset occurredUtc, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken)
        {
            Rejections.Add(eventType);
            return Task.CompletedTask;
        }

        public Task RecordStepUpSuccessAsync(Guid totpFactorId, Guid personId, long timeStep, DateTimeOffset usedUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<TotpFailureOutcome> RecordStepUpFailureAsync(Guid totpFactorId, Guid personId, DateTimeOffset failedUtc, Guid correlationId, string frontendClientId, string? sourceIpAddress, string? clientSourceIpAddress, CancellationToken cancellationToken)
            => Task.FromResult(new TotpFailureOutcome(1, null));
    }

    private sealed class FakeStateStore : IMembershipRequestStateStore
    {
        public List<(MembershipRequestStatus From, MembershipRequestStatus To)> Transitions { get; } = [];
        public Task TransitionAsync(Guid requestId, MembershipRequestStatus expectedStatus, MembershipRequestStatus nextStatus, MembershipRequestTransitionAuditContext auditContext, string reason, CancellationToken cancellationToken)
        {
            Transitions.Add((expectedStatus, nextStatus));
            return Task.CompletedTask;
        }
    }
}
