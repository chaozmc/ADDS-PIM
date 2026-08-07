using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Domain.Security;

namespace ADDS.PIM.Application.Tests.MembershipRequests;

public sealed class ListMyAvailableEntitlementsUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_ReturnsCurrentEntitlementsAndTheirRequirementsWithEffectiveConstraints()
    {
        var allowed = Context();
        var blocked = Context() with { EntitlementId = Guid.NewGuid(), PolicyRequiresSecondFactor = true, PolicyAllowedSecondFactorTypes = SecondFactorType.Totp };
        var useCase = new ListMyAvailableEntitlementsUseCase(new Data([Actor()], [allowed, blocked]), new FixedTimeProvider(Now));

        var result = await useCase.ExecuteAsync(new(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(2, result!.Count);
        var item = Assert.Single(result, item => item.EntitlementId == allowed.EntitlementId);
        Assert.Equal(600, item.DefaultTtlSeconds);
        Assert.Equal(300, item.TtlStepSeconds);
        var mfaItem = Assert.Single(result, item => item.EntitlementId == blocked.EntitlementId);
        Assert.True(mfaItem.RequiresSecondFactor);
    }

    private static ResolvedActorIdentity Actor() => new(Guid.NewGuid(), Guid.NewGuid(), true, true, true, true, Now.AddDays(-1), null, true, true, Now.AddDays(-1), null);
    private static MembershipAuthorizationContext Context() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Alex", "HOME\\alex", "HOME\\alex-admin", "HOME\\ops", true, true, true, true, true, Now.AddDays(-1), null, true, true, true, 300, 1800, 600, 300, false, SecondFactorType.None, false, false, true, Now.AddDays(-1), null, null, null, null, null, null, null);

    private sealed class Data(IReadOnlyList<ResolvedActorIdentity> actors, IReadOnlyList<MembershipAuthorizationContext> contexts) : IMembershipAuthorizationDataSource
    {
        public Task<IReadOnlyList<ResolvedActorIdentity>> ResolveActorAsync(AuthenticatedDirectoryAccount actor, CancellationToken cancellationToken) => Task.FromResult(actors);
        public Task<IReadOnlyList<MembershipAuthorizationContext>> FindContextsAsync(Guid personId, Guid actorAccountId, Guid targetAccountId, Guid targetGroupId, CancellationToken cancellationToken) => Task.FromResult(contexts);
        public Task<IReadOnlyList<MembershipAuthorizationContext>> FindContextsForPersonAsync(Guid personId, Guid actorAccountId, CancellationToken cancellationToken) => Task.FromResult(contexts);
    }
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
