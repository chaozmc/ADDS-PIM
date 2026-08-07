using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Domain.MembershipRequests;

namespace ADDS.PIM.Application.Tests.MembershipRequests;

public sealed class ListMyMembershipRequestsUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_ReturnsOnlyTheResolvedPersonsHistory()
    {
        var personId = Guid.NewGuid();
        var history = new CapturingHistoryStore(CreatePage([CreateRequest(), CreateRequest()]));
        var useCase = new ListMyMembershipRequestsUseCase(new FakeAuthorizationDataSource([CreateActor(personId)]), history, new FixedTimeProvider(Now));

        var result = await useCase.ExecuteAsync(CreateCommand(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(personId, history.PersonId);
        Assert.Equal(10, history.Filter?.PageSize);
    }

    [Fact]
    public async Task ExecuteAsync_DeniesAmbiguousActorOwnershipWithoutReadingHistory()
    {
        var history = new CapturingHistoryStore(CreatePage([]));
        var useCase = new ListMyMembershipRequestsUseCase(
            new FakeAuthorizationDataSource([CreateActor(Guid.NewGuid()), CreateActor(Guid.NewGuid())]),
            history,
            new FixedTimeProvider(Now));

        var result = await useCase.ExecuteAsync(CreateCommand(), CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, history.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsAnUnboundedPageSize()
    {
        var useCase = new ListMyMembershipRequestsUseCase(
            new FakeAuthorizationDataSource([CreateActor(Guid.NewGuid())]),
            new CapturingHistoryStore(CreatePage([])),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ArgumentException>(() => useCase.ExecuteAsync(CreateCommand() with { PageSize = 15 }, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_PassesTheRequestedFiltersOnlyAfterResolvingThePerson()
    {
        var entitlementId = Guid.NewGuid();
        var targetGroupId = Guid.NewGuid();
        var history = new CapturingHistoryStore(CreatePage([]));
        var useCase = new ListMyMembershipRequestsUseCase(new FakeAuthorizationDataSource([CreateActor(Guid.NewGuid())]), history, new FixedTimeProvider(Now));

        await useCase.ExecuteAsync(CreateCommand() with { PageNumber = 2, PageSize = 20, Year = 2026, Month = 8, EntitlementId = entitlementId, TargetGroupId = targetGroupId }, CancellationToken.None);

        Assert.Equal(new MembershipRequestHistoryFilter(2, 20, 2026, 8, entitlementId, targetGroupId), history.Filter);
    }

    private static ListMyMembershipRequestsCommand CreateCommand() => new(new(Guid.NewGuid(), Guid.NewGuid()), 1, 10, null, null, null, null);

    private static ResolvedActorIdentity CreateActor(Guid personId) => new(
        Guid.NewGuid(), personId, true, true, true, true,
        Now.AddDays(-1), null, true, true, Now.AddDays(-1), null);

    private static MembershipRequestHistoryItem CreateRequest() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "HOME\\alex-admin", "HOME\\PIM-Test-Operators", 3_600,
        Now, MembershipRequestStatus.Succeeded, "Planned maintenance", null);

    private static MembershipRequestHistoryPage CreatePage(IReadOnlyList<MembershipRequestHistoryItem> items)
        => new(items, items.Count, [2026], [], []);

    private sealed class FakeAuthorizationDataSource(IReadOnlyList<ResolvedActorIdentity> actors) : IMembershipAuthorizationDataSource
    {
        public Task<IReadOnlyList<ResolvedActorIdentity>> ResolveActorAsync(AuthenticatedDirectoryAccount actor, CancellationToken cancellationToken)
            => Task.FromResult(actors);

        public Task<IReadOnlyList<MembershipAuthorizationContext>> FindContextsAsync(Guid personId, Guid actorAccountId, Guid targetAccountId, Guid targetGroupId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<MembershipAuthorizationContext>> FindContextsForPersonAsync(Guid personId, Guid actorAccountId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class CapturingHistoryStore(MembershipRequestHistoryPage result) : IMyMembershipRequestHistoryStore
    {
        public Guid? PersonId { get; private set; }
        public MembershipRequestHistoryFilter? Filter { get; private set; }
        public int Calls { get; private set; }

        public Task<MembershipRequestHistoryPage> ListForPersonAsync(Guid personId, MembershipRequestHistoryFilter filter, CancellationToken cancellationToken)
        {
            Calls++;
            PersonId = personId;
            Filter = filter;
            return Task.FromResult(result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
