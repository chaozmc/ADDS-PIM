using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Application.Mfa;

namespace ADDS.PIM.Application.Tests.Mfa;

public sealed class GetMyMfaStatusUseCaseTests
{
    private static readonly Guid ActorAccountId = Guid.NewGuid();
    private static readonly Guid PersonId = Guid.NewGuid();
    private static readonly AuthenticatedDirectoryAccount Actor = new(Guid.NewGuid(), ActorAccountId);

    [Fact]
    public async Task ExecuteAsync_ReturnsNullWhenActorCannotBeResolvedToAPerson()
    {
        var useCase = NewUseCase(validActor: false, new TotpStatus(true, DateTimeOffset.UtcNow));

        var result = await useCase.ExecuteAsync(Actor, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNotEnrolledWhenNoActiveTotpFactorExists()
    {
        var useCase = NewUseCase(validActor: true, new TotpStatus(false, null));

        var result = await useCase.ExecuteAsync(Actor, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.TotpEnrolled);
        Assert.Null(result.TotpConfirmedUtc);
        Assert.True(result.Fido2Available);
        Assert.False(result.Fido2Enrolled);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsEnrolledWithConfirmationTimeWhenAnActiveTotpFactorExists()
    {
        var confirmedUtc = DateTimeOffset.UtcNow.AddDays(-3);
        var useCase = NewUseCase(validActor: true, new TotpStatus(true, confirmedUtc));

        var result = await useCase.ExecuteAsync(Actor, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.TotpEnrolled);
        Assert.Equal(confirmedUtc, result.TotpConfirmedUtc);
    }

    private static GetMyMfaStatusUseCase NewUseCase(bool validActor, TotpStatus totpStatus)
    {
        var dataSource = new FakeAuthorizationDataSource(validActor);
        var resolvePerson = new ResolveCurrentPersonUseCase(dataSource, TimeProvider.System);
        return new GetMyMfaStatusUseCase(resolvePerson, new FakeMfaStatusStore(totpStatus));
    }

    private sealed class FakeAuthorizationDataSource(bool validActor) : IMembershipAuthorizationDataSource
    {
        public Task<IReadOnlyList<ResolvedActorIdentity>> ResolveActorAsync(AuthenticatedDirectoryAccount actor, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ResolvedActorIdentity>>(validActor
                ? [new(ActorAccountId, PersonId, true, true, true, true, DateTimeOffset.UtcNow.AddDays(-1), null, true, true, DateTimeOffset.UtcNow.AddDays(-1), null)]
                : []);

        public Task<IReadOnlyList<MembershipAuthorizationContext>> FindContextsAsync(Guid personId, Guid actorAccountId, Guid targetAccountId, Guid targetGroupId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<MembershipAuthorizationContext>> FindContextsForPersonAsync(Guid personId, Guid actorAccountId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class FakeMfaStatusStore(TotpStatus status) : IMfaStatusStore
    {
        public Task<TotpStatus> FindTotpStatusAsync(Guid personId, CancellationToken cancellationToken) => Task.FromResult(status);
        public Task<Fido2Status> FindFido2StatusAsync(Guid personId, CancellationToken cancellationToken) => Task.FromResult(new Fido2Status(false, []));
    }
}
