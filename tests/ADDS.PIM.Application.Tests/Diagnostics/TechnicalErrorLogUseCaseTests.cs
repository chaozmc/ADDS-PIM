using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Application.Diagnostics;
using ADDS.PIM.Contracts.Administration.V1;

namespace ADDS.PIM.Application.Tests.Diagnostics;

public sealed class TechnicalErrorLogUseCaseTests
{
    private static readonly Guid ScopeId = Guid.NewGuid();
    private static readonly AdministrationActor Actor = new(ScopeId, Guid.NewGuid());

    [Fact]
    public async Task QueryAsync_RejectsActorFromAnotherDirectoryScope()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);

        var result = await useCase.QueryAsync(new(new(Guid.NewGuid(), Actor.ObjectGuid), 1, 20, null, null, null, null), CancellationToken.None);

        Assert.Null(result);
        Assert.Null(store.LastFilter);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 15)]
    public async Task QueryAsync_RejectsInvalidPaging(int pageNumber, int pageSize)
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);

        var result = await useCase.QueryAsync(new(Actor, pageNumber, pageSize, null, null, null, null), CancellationToken.None);

        Assert.Null(result);
        Assert.Null(store.LastFilter);
    }

    [Fact]
    public async Task QueryAsync_RejectsFromUtcAfterToUtc()
    {
        var store = new FakeStore();
        var useCase = CreateUseCase(store);
        var now = DateTimeOffset.UtcNow;

        var result = await useCase.QueryAsync(new(Actor, 1, 20, now, now.AddDays(-1), null, null), CancellationToken.None);

        Assert.Null(result);
        Assert.Null(store.LastFilter);
    }

    [Fact]
    public async Task QueryAsync_ForwardsValidatedFilterToStore()
    {
        var store = new FakeStore { Page = new([], 0) };
        var useCase = CreateUseCase(store);
        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow;
        var requestId = Guid.NewGuid();

        var result = await useCase.QueryAsync(new(Actor, 2, 30, from, to, requestId, null), CancellationToken.None);

        Assert.Same(store.Page, result);
        Assert.NotNull(store.LastFilter);
        Assert.Equal(2, store.LastFilter!.PageNumber);
        Assert.Equal(30, store.LastFilter.PageSize);
        Assert.Equal(from, store.LastFilter.FromUtc);
        Assert.Equal(to, store.LastFilter.ToUtc);
        Assert.Equal(requestId, store.LastFilter.RequestId);
    }

    private static TechnicalErrorLogUseCase CreateUseCase(FakeStore store)
        => new(store, new DirectoryScopeConfiguration(ScopeId, "example.org", "example.org"));

    private sealed class FakeStore : ITechnicalErrorLogStore
    {
        public TechnicalErrorLogFilter? LastFilter { get; private set; }
        public TechnicalErrorLogRecordPage Page { get; init; } = new([], 0);

        public Task RecordAsync(NewTechnicalErrorLogEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<TechnicalErrorLogRecordPage> QueryAsync(TechnicalErrorLogFilter filter, CancellationToken cancellationToken)
        {
            LastFilter = filter;
            return Task.FromResult(Page);
        }
    }
}
