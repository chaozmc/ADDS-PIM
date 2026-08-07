using ADDS.PIM.Application.Diagnostics;

namespace ADDS.PIM.Application.Tests.Diagnostics;

public sealed class RecordTechnicalErrorUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ForwardsEntryToStore()
    {
        var store = new FakeStore();
        var useCase = new RecordTechnicalErrorUseCase(store);
        var entry = new NewTechnicalErrorLogEntry(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid(),
            "POST", "/api/v1/membership-requests", 500, "System.ArgumentException", "boom", "trace", "Api");

        await useCase.ExecuteAsync(entry, CancellationToken.None);

        Assert.Same(entry, store.LastEntry);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsForNullEntry()
        => await Assert.ThrowsAsync<ArgumentNullException>(() => new RecordTechnicalErrorUseCase(new FakeStore()).ExecuteAsync(null!, CancellationToken.None));

    private sealed class FakeStore : ITechnicalErrorLogStore
    {
        public NewTechnicalErrorLogEntry? LastEntry { get; private set; }

        public Task RecordAsync(NewTechnicalErrorLogEntry entry, CancellationToken cancellationToken)
        {
            LastEntry = entry;
            return Task.CompletedTask;
        }

        public Task<TechnicalErrorLogRecordPage> QueryAsync(TechnicalErrorLogFilter filter, CancellationToken cancellationToken)
            => Task.FromResult(new TechnicalErrorLogRecordPage([], 0));
    }
}
