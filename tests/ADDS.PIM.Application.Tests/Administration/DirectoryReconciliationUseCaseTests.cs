using ADDS.PIM.Application.Administration;
using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Contracts.Administration.V1;

namespace ADDS.PIM.Application.Tests.Administration;

public sealed class DirectoryReconciliationUseCaseTests
{
    private static readonly Guid ScopeId = Guid.NewGuid();
    private static readonly AdministrationActor Actor = new(ScopeId, Guid.NewGuid());

    [Fact]
    public async Task QueueAsync_RejectsActorFromAnotherDirectoryScope()
    {
        var store = new FakeStore(); var useCase = CreateUseCase(store, new FakeResolver());

        var result = await useCase.QueueAsync(new(new(Guid.NewGuid(), Actor.ObjectGuid)), new(Guid.NewGuid(), "test", null), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.False(store.Queued);
    }

    [Fact]
    public async Task ExecuteNextAsync_RecordsOnlyConfirmedDirectoryFindings()
    {
        var account = Candidate(DirectoryReconciliationEntityType.DirectoryAccount);
        var group = Candidate(DirectoryReconciliationEntityType.TargetGroup);
        var store = new FakeStore { WorkItem = new(Guid.NewGuid()), Candidates = [account, group] };
        var resolver = new FakeResolver
        {
            AccountResult = new(DirectoryObjectLookupStatus.Found, false, true),
            GroupResult = new(DirectoryObjectLookupStatus.NotFound, false, false)
        };
        var useCase = CreateUseCase(store, resolver);

        var processed = await useCase.ExecuteNextAsync(CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(2, store.Findings.Count);
        Assert.Contains(store.Findings, finding => finding.Candidate == account && finding.Reason == DirectoryReconciliationFindingReason.DirectoryObjectDisabled);
        Assert.Contains(store.Findings, finding => finding.Candidate == group && finding.Reason == DirectoryReconciliationFindingReason.DirectoryObjectDeleted);
        Assert.True(store.Completed);
        Assert.Null(store.FailureCategory);
    }

    [Fact]
    public async Task ExecuteNextAsync_FailsRunWithoutCreatingAbsenceFindingWhenDirectoryLookupFails()
    {
        var store = new FakeStore { WorkItem = new(Guid.NewGuid()), Candidates = [Candidate(DirectoryReconciliationEntityType.DirectoryAccount)] };
        var useCase = CreateUseCase(store, new FakeResolver { ThrowOnAccountLookup = true });

        var processed = await useCase.ExecuteNextAsync(CancellationToken.None);

        Assert.True(processed);
        Assert.Empty(store.Findings);
        Assert.False(store.Completed);
        Assert.Equal("ActiveDirectory", store.FailureCategory);
    }

    [Fact]
    public async Task DeactivateFromFindingAsync_RejectsActorFromAnotherDirectoryScope()
    {
        var store = new FakeStore(); var useCase = CreateUseCase(store, new FakeResolver());

        var result = await useCase.DeactivateFromFindingAsync(new(new(Guid.NewGuid(), Actor.ObjectGuid), Guid.NewGuid(), "AQ=="), new(Guid.NewGuid(), "test", null), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Invalid, result);
        Assert.Null(store.DeactivationRequest);
    }

    [Fact]
    public async Task DeactivateFromFindingAsync_ForwardsValidatedFindingToStore()
    {
        var store = new FakeStore(); var useCase = CreateUseCase(store, new FakeResolver());
        var request = new DeactivateDirectoryReconciliationFindingRequest(Actor, Guid.NewGuid(), "AQ==");

        var result = await useCase.DeactivateFromFindingAsync(request, new(Guid.NewGuid(), "test", null), CancellationToken.None);

        Assert.Equal(AdministrationUpdateResult.Updated, result);
        Assert.Equal(request, store.DeactivationRequest);
    }

    private static DirectoryReconciliationUseCase CreateUseCase(FakeStore store, FakeResolver resolver)
        => new(store, resolver, new DirectoryScopeConfiguration(ScopeId, "example.org", "example.org"), TimeProvider.System);

    private static ReconciliationCandidate Candidate(DirectoryReconciliationEntityType type)
        => new(type, Guid.NewGuid(), ScopeId, Guid.NewGuid(), type.ToString());

    private sealed class FakeResolver : IDirectoryReconciliationResolver
    {
        public DirectoryObjectLookupResult AccountResult { get; init; } = new(DirectoryObjectLookupStatus.Found, true, true);
        public DirectoryObjectLookupResult GroupResult { get; init; } = new(DirectoryObjectLookupStatus.Found, true, true);
        public bool ThrowOnAccountLookup { get; init; }
        public Task<DirectoryObjectLookupResult> ResolveAccountAsync(Guid objectGuid, CancellationToken cancellationToken)
            => ThrowOnAccountLookup ? throw new InvalidOperationException("Directory unavailable.") : Task.FromResult(AccountResult);
        public Task<DirectoryObjectLookupResult> ResolveGroupAsync(Guid objectGuid, CancellationToken cancellationToken) => Task.FromResult(GroupResult);
    }

    private sealed class FakeStore : IDirectoryReconciliationStore
    {
        public bool Queued { get; private set; }
        public ReconciliationWorkItem? WorkItem { get; init; }
        public IReadOnlyList<ReconciliationCandidate> Candidates { get; init; } = [];
        public List<(ReconciliationCandidate Candidate, DirectoryReconciliationFindingReason Reason)> Findings { get; } = [];
        public bool Completed { get; private set; }
        public string? FailureCategory { get; private set; }
        public DeactivateDirectoryReconciliationFindingRequest? DeactivationRequest { get; private set; }
        public Task<AdministrationUpdateResult> QueueAsync(AdministrationActor actor, AdministrationAuditContext auditContext, CancellationToken cancellationToken) { Queued = true; return Task.FromResult(AdministrationUpdateResult.Updated); }
        public Task<ReconciliationWorkItem?> ClaimNextAsync(CancellationToken cancellationToken) => Task.FromResult(WorkItem);
        public Task<IReadOnlyList<ReconciliationCandidate>> ListCandidatesAsync(Guid runId, CancellationToken cancellationToken) => Task.FromResult(Candidates);
        public Task AddFindingAsync(Guid runId, ReconciliationCandidate candidate, DirectoryReconciliationFindingReason reason, DateTimeOffset detectedUtc, CancellationToken cancellationToken) { Findings.Add((candidate, reason)); return Task.CompletedTask; }
        public Task CompleteAsync(Guid runId, DateTimeOffset completedUtc, CancellationToken cancellationToken) { Completed = true; return Task.CompletedTask; }
        public Task FailAsync(Guid runId, string failureCategory, DateTimeOffset completedUtc, CancellationToken cancellationToken) { FailureCategory = failureCategory; return Task.CompletedTask; }
        public Task<IReadOnlyList<DirectoryReconciliationRun>> ListRunsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DirectoryReconciliationRun>>([]);
        public Task<IReadOnlyList<DirectoryReconciliationFinding>> ListFindingsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DirectoryReconciliationFinding>>([]);
        public Task<AdministrationUpdateResult> DeactivateFromFindingAsync(DeactivateDirectoryReconciliationFindingRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken) { DeactivationRequest = request; return Task.FromResult(AdministrationUpdateResult.Updated); }
    }
}
