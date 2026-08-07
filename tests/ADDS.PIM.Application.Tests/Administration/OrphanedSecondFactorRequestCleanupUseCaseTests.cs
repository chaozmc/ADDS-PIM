using ADDS.PIM.Application.Administration;
using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Contracts.Administration.V1;

namespace ADDS.PIM.Application.Tests.Administration;

public sealed class OrphanedSecondFactorRequestCleanupUseCaseTests
{
    private static readonly Guid ScopeId = Guid.NewGuid();
    private static readonly AdministrationActor Actor = new(ScopeId, Guid.NewGuid());
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task ListCandidatesAsync_RejectsAnActorFromAnotherScope()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);

        var result = await useCase.ListCandidatesAsync(new(Guid.NewGuid(), Actor.ObjectGuid), CancellationToken.None);

        Assert.Empty(result);
        Assert.False(store.ListCalled);
    }

    [Fact]
    public async Task ListCandidatesAsync_ForwardsToStore()
    {
        var candidate = new OrphanedSecondFactorRequestCandidate(Guid.NewGuid(), "Alice", "Target", "Group", Now.AddMinutes(-10), Now.AddMinutes(-5));
        var store = new FakeStore { Candidates = [candidate] };
        var useCase = CreateUseCase(store);

        var result = await useCase.ListCandidatesAsync(Actor, CancellationToken.None);

        Assert.Same(candidate, Assert.Single(result));
        Assert.True(store.ListCalled);
    }

    [Fact]
    public async Task ExpireAsync_RejectsAnActorFromAnotherScope()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);

        var result = await useCase.ExpireAsync(new(Guid.NewGuid(), Actor.ObjectGuid), Guid.NewGuid(), new(Guid.NewGuid(), "test", "Windows", "summary", null), CancellationToken.None);

        Assert.Equal(OrphanedSecondFactorRequestCleanupResult.NotFound, result);
        Assert.False(store.ExpireCalled);
    }

    [Fact]
    public async Task ExpireAsync_ForwardsToStore()
    {
        var requestId = Guid.NewGuid();
        var store = new FakeStore { ExpireResult = OrphanedSecondFactorRequestCleanupResult.Expired };
        var useCase = CreateUseCase(store);

        var result = await useCase.ExpireAsync(Actor, requestId, new(Guid.NewGuid(), "test", "Windows", "summary", null), CancellationToken.None);

        Assert.Equal(OrphanedSecondFactorRequestCleanupResult.Expired, result);
        Assert.True(store.ExpireCalled);
        Assert.Equal(requestId, store.LastRequestId);
        Assert.Equal(Actor, store.LastAdministrator);
    }

    private static OrphanedSecondFactorRequestCleanupUseCase CreateUseCase(FakeStore store)
        => new(store, new DirectoryScopeConfiguration(ScopeId, "example.org", "example.org"), TimeProvider.System);

    private sealed class FakeStore : IOrphanedSecondFactorRequestStore
    {
        public bool ListCalled { get; private set; }
        public bool ExpireCalled { get; private set; }
        public Guid? LastRequestId { get; private set; }
        public AdministrationActor? LastAdministrator { get; private set; }
        public IReadOnlyList<OrphanedSecondFactorRequestCandidate> Candidates { get; init; } = [];
        public OrphanedSecondFactorRequestCleanupResult ExpireResult { get; init; } = OrphanedSecondFactorRequestCleanupResult.NotFound;

        public Task<IReadOnlyList<OrphanedSecondFactorRequestCandidate>> ListCandidatesAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            ListCalled = true;
            return Task.FromResult(Candidates);
        }

        public Task<OrphanedSecondFactorRequestCleanupResult> ExpireAsync(Guid requestId, DateTimeOffset now, AdministrationActor administrator, MembershipRequestTransitionAuditContext auditContext, CancellationToken cancellationToken)
        {
            ExpireCalled = true;
            LastRequestId = requestId;
            LastAdministrator = administrator;
            return Task.FromResult(ExpireResult);
        }
    }
}
