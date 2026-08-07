using ADDS.PIM.Contracts.Administration.V1;
using ADDS.PIM.Application.Authorization;

namespace ADDS.PIM.Application.Administration;

public enum DirectoryReconciliationRunStatus { Queued, Executing, Succeeded, Failed }
public enum DirectoryReconciliationEntityType { DirectoryAccount, TargetGroup }
public enum DirectoryReconciliationFindingReason { DirectoryObjectDeleted, DirectoryObjectDisabled, DirectoryObjectOutOfScope }

public sealed record DirectoryReconciliationRun(
    Guid RunId,
    DirectoryReconciliationRunStatus Status,
    DateTimeOffset RequestedUtc,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? CompletedUtc,
    string? FailureCategory,
    string RowVersion);

public sealed record DirectoryReconciliationFinding(
    Guid FindingId,
    Guid RunId,
    DirectoryReconciliationEntityType EntityType,
    Guid EntityId,
    Guid DirectoryScopeId,
    Guid ObjectGuid,
    DirectoryReconciliationFindingReason Reason,
    string DisplayName,
    DateTimeOffset DetectedUtc,
    bool IsResolved,
    DateTimeOffset? ResolvedUtc,
    DateTimeOffset? DeactivatedUtc,
    string RowVersion);

public sealed record ReconciliationCandidate(
    DirectoryReconciliationEntityType EntityType,
    Guid EntityId,
    Guid DirectoryScopeId,
    Guid ObjectGuid,
    string DisplayName);

public enum DirectoryObjectLookupStatus { Found, NotFound, Ambiguous }
public sealed record DirectoryObjectLookupResult(DirectoryObjectLookupStatus Status, bool IsEnabled, bool IsWithinAllowedScope);

public interface IDirectoryReconciliationResolver
{
    Task<DirectoryObjectLookupResult> ResolveAccountAsync(Guid objectGuid, CancellationToken cancellationToken);
    Task<DirectoryObjectLookupResult> ResolveGroupAsync(Guid objectGuid, CancellationToken cancellationToken);
}

public interface IDirectoryReconciliationStore
{
    Task<AdministrationUpdateResult> QueueAsync(AdministrationActor actor, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<ReconciliationWorkItem?> ClaimNextAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ReconciliationCandidate>> ListCandidatesAsync(Guid runId, CancellationToken cancellationToken);
    Task AddFindingAsync(Guid runId, ReconciliationCandidate candidate, DirectoryReconciliationFindingReason reason, DateTimeOffset detectedUtc, CancellationToken cancellationToken);
    Task CompleteAsync(Guid runId, DateTimeOffset completedUtc, CancellationToken cancellationToken);
    Task FailAsync(Guid runId, string failureCategory, DateTimeOffset completedUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<DirectoryReconciliationRun>> ListRunsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<DirectoryReconciliationFinding>> ListFindingsAsync(CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> DeactivateFromFindingAsync(DeactivateDirectoryReconciliationFindingRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
}

public sealed record ReconciliationWorkItem(Guid RunId);

public sealed class DirectoryReconciliationUseCase(
    IDirectoryReconciliationStore store,
    IDirectoryReconciliationResolver directoryResolver,
    DirectoryScopeConfiguration directoryScope,
    TimeProvider timeProvider)
{
    public Task<AdministrationUpdateResult> QueueAsync(StartDirectoryReconciliationRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => request.Actor.DirectoryScopeId != directoryScope.DirectoryScopeId || request.Actor.ObjectGuid == Guid.Empty
            ? Task.FromResult(AdministrationUpdateResult.Invalid)
            : store.QueueAsync(request.Actor, auditContext, cancellationToken);

    public Task<IReadOnlyList<DirectoryReconciliationRun>> ListRunsAsync(CancellationToken cancellationToken)
        => store.ListRunsAsync(cancellationToken);

    public Task<IReadOnlyList<DirectoryReconciliationFinding>> ListFindingsAsync(CancellationToken cancellationToken)
        => store.ListFindingsAsync(cancellationToken);

    public Task<AdministrationUpdateResult> DeactivateFromFindingAsync(DeactivateDirectoryReconciliationFindingRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => request.Actor.DirectoryScopeId != directoryScope.DirectoryScopeId || request.Actor.ObjectGuid == Guid.Empty || request.FindingId == Guid.Empty || string.IsNullOrWhiteSpace(request.RowVersion)
            ? Task.FromResult(AdministrationUpdateResult.Invalid)
            : store.DeactivateFromFindingAsync(request, auditContext, cancellationToken);

    public async Task<bool> ExecuteNextAsync(CancellationToken cancellationToken)
    {
        var workItem = await store.ClaimNextAsync(cancellationToken);
        if (workItem is null) return false;

        try
        {
            var candidates = await store.ListCandidatesAsync(workItem.RunId, cancellationToken);
            foreach (var candidate in candidates)
            {
                var result = candidate.EntityType == DirectoryReconciliationEntityType.DirectoryAccount
                    ? await directoryResolver.ResolveAccountAsync(candidate.ObjectGuid, cancellationToken)
                    : await directoryResolver.ResolveGroupAsync(candidate.ObjectGuid, cancellationToken);

                var finding = ToFindingReason(candidate.EntityType, result);
                if (finding is not null)
                    await store.AddFindingAsync(workItem.RunId, candidate, finding.Value, timeProvider.GetUtcNow(), cancellationToken);
            }
            await store.CompleteAsync(workItem.RunId, timeProvider.GetUtcNow(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            await store.FailAsync(workItem.RunId, "ActiveDirectory", timeProvider.GetUtcNow(), CancellationToken.None);
        }

        return true;
    }

    private static DirectoryReconciliationFindingReason? ToFindingReason(DirectoryReconciliationEntityType entityType, DirectoryObjectLookupResult result)
    {
        if (result.Status == DirectoryObjectLookupStatus.NotFound) return DirectoryReconciliationFindingReason.DirectoryObjectDeleted;
        if (result.Status == DirectoryObjectLookupStatus.Ambiguous) throw new InvalidOperationException("Directory object lookup is ambiguous.");
        if (!result.IsWithinAllowedScope) return DirectoryReconciliationFindingReason.DirectoryObjectOutOfScope;
        if (entityType == DirectoryReconciliationEntityType.DirectoryAccount && !result.IsEnabled) return DirectoryReconciliationFindingReason.DirectoryObjectDisabled;
        return null;
    }
}
