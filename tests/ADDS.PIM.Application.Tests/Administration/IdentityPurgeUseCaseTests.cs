using ADDS.PIM.Application.Administration;
using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Contracts.Administration.V1;

namespace ADDS.PIM.Application.Tests.Administration;

public sealed class IdentityPurgeUseCaseTests
{
    private static readonly Guid ScopeId = Guid.NewGuid();
    private static readonly AdministrationActor Actor = new(ScopeId, Guid.NewGuid());

    [Fact]
    public async Task GetPreviewAsync_RejectsAnActorFromAnotherScope()
    {
        var store = new FakeStore(); var useCase = new IdentityPurgeUseCase(store, new FakeEventLog(), new DirectoryScopeConfiguration(ScopeId, "example.org", "example.org"));

        var result = await useCase.GetPreviewAsync(new(Guid.NewGuid(), Actor.ObjectGuid), PurgeInitiatorType.Person, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
        Assert.False(store.Called);
    }

    [Fact]
    public async Task ExecuteAsync_WritesIntentBeforeExecutingTheRecheckedScope()
    {
        var id = Guid.NewGuid(); var preview = new PurgeScopePreview(PurgeInitiatorType.Person, id, "Test", true, null, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "scope=test");
        var store = new FakeStore { Preview = preview }; var eventLog = new FakeEventLog();
        var useCase = new IdentityPurgeUseCase(store, eventLog, new DirectoryScopeConfiguration(ScopeId, "example.org", "example.org"));

        var result = await useCase.ExecuteAsync(Actor, PurgeInitiatorType.Person, id, $"PURGE {id:D}", new(Guid.NewGuid(), "test", null), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Updated, result);
        Assert.True(eventLog.IntentWritten);
        Assert.True(store.Executed);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotExecuteWhenMandatoryIntentWriteFails()
    {
        var id = Guid.NewGuid(); var preview = new PurgeScopePreview(PurgeInitiatorType.Person, id, "Test", true, null, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "scope=test");
        var store = new FakeStore { Preview = preview }; var eventLog = new FakeEventLog { ThrowOnWrite = true };
        var useCase = new IdentityPurgeUseCase(store, eventLog, new DirectoryScopeConfiguration(ScopeId, "example.org", "example.org"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(Actor, PurgeInitiatorType.Person, id, $"PURGE {id:D}", new(Guid.NewGuid(), "test", null), CancellationToken.None));

        Assert.False(store.Executed);
    }

    private sealed class FakeStore : IIdentityPurgeScopeStore
    {
        public bool Called { get; private set; }
        public bool Executed { get; private set; }
        public PurgeScopePreview? Preview { get; init; }
        public Task<IReadOnlyList<ADDS.PIM.Application.Administration.IdentityPurgeCandidate>> ListCandidatesAsync(Guid directoryScopeId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ADDS.PIM.Application.Administration.IdentityPurgeCandidate>>([]);
        public Task<PurgeScopePreview?> GetPreviewAsync(PurgeInitiatorType initiatorType, Guid initiatorId, Guid directoryScopeId, CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(Preview);
        }
        public Task<AdministrationUpdateResult> ExecuteAsync(PurgeInitiatorType initiatorType, Guid initiatorId, AdministrationActor actor, AdministrationAuditContext auditContext, CancellationToken cancellationToken) { Executed = true; return Task.FromResult(AdministrationUpdateResult.Updated); }
    }

    private sealed class FakeEventLog : ADDS.PIM.Application.Audit.IPurgeEventLog
    {
        public bool IntentWritten { get; private set; }
        public bool ThrowOnWrite { get; init; }
        public Task VerifyWritableAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteAsync(ADDS.PIM.Application.Audit.PurgeEventLogEntry entry, CancellationToken cancellationToken) { if (ThrowOnWrite) throw new InvalidOperationException(); IntentWritten = true; return Task.CompletedTask; }
    }
}
