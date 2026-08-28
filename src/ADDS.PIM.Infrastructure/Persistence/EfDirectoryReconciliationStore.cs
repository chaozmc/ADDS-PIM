using ADDS.PIM.Application.Administration;
using ADDS.PIM.Contracts.Administration.V1;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfDirectoryReconciliationStore(PimDbContext dbContext, TimeProvider timeProvider, ILogger<EfDirectoryReconciliationStore> logger) : IDirectoryReconciliationStore
{
    public async Task<AdministrationUpdateResult> QueueAsync(AdministrationActor actor, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (await dbContext.DirectoryReconciliationRuns.AnyAsync(run => run.Status == DirectoryReconciliationRunStatus.Queued || run.Status == DirectoryReconciliationRunStatus.Executing, cancellationToken))
        {
            logger.LogWarning("QueueAsync conflict: a reconciliation run is already Queued or Executing for DirectoryScopeId {DirectoryScopeId}.", actor.DirectoryScopeId);
            return AdministrationUpdateResult.Conflict;
        }

        var now = timeProvider.GetUtcNow();
        var run = new DirectoryReconciliationRunEntity
        {
            RunId = Guid.NewGuid(), DirectoryScopeId = actor.DirectoryScopeId, RequestedByObjectGuid = actor.ObjectGuid,
            CorrelationId = auditContext.CorrelationId, FrontendClientId = auditContext.FrontendClientId,
            SourceIpAddress = auditContext.SourceIpAddress, Status = DirectoryReconciliationRunStatus.Queued, RequestedUtc = now
        };
        dbContext.DirectoryReconciliationRuns.Add(run);
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            EventId = Guid.NewGuid(), EventType = "DirectoryReconciliationQueued", OccurredUtc = now,
            FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress,
            ClientSourceIpAddress = auditContext.ClientSourceIpAddress,
            SourceComponent = "Api", CorrelationId = auditContext.CorrelationId, Result = "Succeeded",
            AuthenticationMethod = "Windows", PolicyRequirementsSummary = $"run:{run.RunId:D};scope:{actor.DirectoryScopeId:D}"
        });
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "QueueAsync failed to save the new reconciliation run for DirectoryScopeId {DirectoryScopeId}.", actor.DirectoryScopeId); return AdministrationUpdateResult.Conflict; }
    }

    public async Task<ReconciliationWorkItem?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var run = await dbContext.DirectoryReconciliationRuns.OrderBy(item => item.RequestedUtc)
            .FirstOrDefaultAsync(item => item.Status == DirectoryReconciliationRunStatus.Queued, cancellationToken);
        if (run is null) return null;
        run.Status = DirectoryReconciliationRunStatus.Executing;
        run.StartedUtc = timeProvider.GetUtcNow();
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ReconciliationWorkItem(run.RunId);
        }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "ClaimNextAsync failed to claim RunId {RunId}; rolled back.", run.RunId); await transaction.RollbackAsync(cancellationToken); return null; }
    }

    public async Task<IReadOnlyList<ReconciliationCandidate>> ListCandidatesAsync(Guid runId, CancellationToken cancellationToken)
    {
        var scopeId = await dbContext.DirectoryReconciliationRuns.Where(run => run.RunId == runId && run.Status == DirectoryReconciliationRunStatus.Executing)
            .Select(run => (Guid?)run.DirectoryScopeId).SingleOrDefaultAsync(cancellationToken);
        if (scopeId is null) return [];

        var accounts = await dbContext.DirectoryAccounts.AsNoTracking().Where(account => account.DirectoryScopeId == scopeId && account.IsActive)
            .Select(account => new ReconciliationCandidate(DirectoryReconciliationEntityType.DirectoryAccount, account.AccountId, account.DirectoryScopeId, account.ObjectGuid, account.DomainQualifiedName)).ToArrayAsync(cancellationToken);
        var groups = await (from targetGroup in dbContext.TargetGroups.AsNoTracking()
                            join policy in dbContext.GroupPolicies.AsNoTracking() on targetGroup.GroupPolicyId equals policy.GroupPolicyId
                            where targetGroup.DirectoryScopeId == scopeId && targetGroup.IsEnabledForRequests && policy.IsActive
                            select new ReconciliationCandidate(DirectoryReconciliationEntityType.TargetGroup, targetGroup.TargetGroupId, targetGroup.DirectoryScopeId, targetGroup.ObjectGuid, targetGroup.DomainQualifiedName)).ToArrayAsync(cancellationToken);
        return [.. accounts, .. groups];
    }

    public async Task AddFindingAsync(Guid runId, ReconciliationCandidate candidate, DirectoryReconciliationFindingReason reason, DateTimeOffset detectedUtc, CancellationToken cancellationToken)
    {
        dbContext.DirectoryReconciliationFindings.Add(new DirectoryReconciliationFindingEntity
        {
            FindingId = Guid.NewGuid(), RunId = runId, EntityType = candidate.EntityType, EntityId = candidate.EntityId,
            DirectoryScopeId = candidate.DirectoryScopeId, ObjectGuid = candidate.ObjectGuid, Reason = reason,
            DisplayNameSnapshot = candidate.DisplayName, DetectedUtc = detectedUtc
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task CompleteAsync(Guid runId, DateTimeOffset completedUtc, CancellationToken cancellationToken)
        => SetFinalStatusAsync(runId, DirectoryReconciliationRunStatus.Succeeded, null, completedUtc, cancellationToken);

    public Task FailAsync(Guid runId, string failureCategory, DateTimeOffset completedUtc, CancellationToken cancellationToken)
        => SetFinalStatusAsync(runId, DirectoryReconciliationRunStatus.Failed, failureCategory, completedUtc, cancellationToken);

    public async Task<IReadOnlyList<DirectoryReconciliationRun>> ListRunsAsync(CancellationToken cancellationToken)
    {
        var runs = await dbContext.DirectoryReconciliationRuns.AsNoTracking().OrderByDescending(run => run.RequestedUtc).ToArrayAsync(cancellationToken);
        return runs.Select(run => new DirectoryReconciliationRun(run.RunId, run.Status, run.RequestedUtc, run.StartedUtc, run.CompletedUtc, run.FailureCategory, Convert.ToBase64String(run.RowVersion))).ToArray();
    }

    public async Task<IReadOnlyList<DirectoryReconciliationFinding>> ListFindingsAsync(CancellationToken cancellationToken)
    {
        var findings = await dbContext.DirectoryReconciliationFindings.AsNoTracking().OrderBy(finding => finding.IsResolved).ThenByDescending(finding => finding.DetectedUtc).ToArrayAsync(cancellationToken);
        return findings.Select(finding => new DirectoryReconciliationFinding(finding.FindingId, finding.RunId, finding.EntityType, finding.EntityId, finding.DirectoryScopeId, finding.ObjectGuid, finding.Reason, finding.DisplayNameSnapshot, finding.DetectedUtc, finding.IsResolved, finding.ResolvedUtc, finding.DeactivatedUtc, Convert.ToBase64String(finding.RowVersion))).ToArray();
    }

    public async Task<AdministrationUpdateResult> DeactivateFromFindingAsync(DeactivateDirectoryReconciliationFindingRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        byte[] findingRowVersion;
        try { findingRowVersion = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { return AdministrationUpdateResult.Invalid; }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var finding = await dbContext.DirectoryReconciliationFindings.SingleOrDefaultAsync(item => item.FindingId == request.FindingId, cancellationToken);
        if (finding is null) return AdministrationUpdateResult.NotFound;
        if (!finding.RowVersion.SequenceEqual(findingRowVersion) || finding.IsResolved || finding.DirectoryScopeId != request.Actor.DirectoryScopeId)
        {
            logger.LogWarning("DeactivateFromFindingAsync conflict for FindingId {FindingId}: RowVersionMismatch={RowVersionMismatch}, AlreadyResolved={AlreadyResolved}, DirectoryScopeMismatch={DirectoryScopeMismatch}.",
                request.FindingId, !finding.RowVersion.SequenceEqual(findingRowVersion), finding.IsResolved, finding.DirectoryScopeId != request.Actor.DirectoryScopeId);
            return AdministrationUpdateResult.Conflict;
        }

        var now = timeProvider.GetUtcNow();
        if (finding.EntityType == DirectoryReconciliationEntityType.DirectoryAccount)
        {
            var account = await dbContext.DirectoryAccounts.SingleOrDefaultAsync(item => item.AccountId == finding.EntityId && item.DirectoryScopeId == finding.DirectoryScopeId && item.ObjectGuid == finding.ObjectGuid, cancellationToken);
            if (account is null) return AdministrationUpdateResult.NotFound;
            if (!account.IsActive) return AdministrationUpdateResult.Conflict;
            account.IsActive = false; account.ModifiedUtc = now;
            var links = await dbContext.PersonAccountLinks.Where(link => link.AccountId == account.AccountId && link.IsActive).ToArrayAsync(cancellationToken);
            foreach (var link in links) { link.IsActive = false; link.ModifiedUtc = now; link.ModifiedBy = auditContext.FrontendClientId; }
            var entitlements = await dbContext.DirectEntitlements.Where(entitlement => entitlement.TargetAccountId == account.AccountId && entitlement.IsActive).ToArrayAsync(cancellationToken);
            foreach (var entitlement in entitlements) { entitlement.IsActive = false; entitlement.ModifiedUtc = now; entitlement.ModifiedBy = auditContext.FrontendClientId; }
            dbContext.AuditEvents.Add(CreateAuditEvent("DirectoryReconciliationAccountDeactivated", finding, request.Actor.ObjectGuid, auditContext, now, account.DomainQualifiedName, null));
        }
        else
        {
            var group = await dbContext.TargetGroups.SingleOrDefaultAsync(item => item.TargetGroupId == finding.EntityId && item.DirectoryScopeId == finding.DirectoryScopeId && item.ObjectGuid == finding.ObjectGuid, cancellationToken);
            if (group is null) return AdministrationUpdateResult.NotFound;
            var policy = await dbContext.GroupPolicies.SingleOrDefaultAsync(item => item.GroupPolicyId == group.GroupPolicyId, cancellationToken);
            if (policy is null) return AdministrationUpdateResult.NotFound;
            if (!group.IsEnabledForRequests || !policy.IsActive)
            {
                logger.LogWarning("DeactivateFromFindingAsync conflict for FindingId {FindingId}: GroupAlreadyDisabled={GroupAlreadyDisabled}, PolicyAlreadyInactive={PolicyAlreadyInactive}.",
                    request.FindingId, !group.IsEnabledForRequests, !policy.IsActive);
                return AdministrationUpdateResult.Conflict;
            }
            group.IsEnabledForRequests = false; group.ModifiedUtc = now; policy.IsActive = false; policy.ModifiedUtc = now;
            var entitlements = await dbContext.DirectEntitlements.Where(entitlement => entitlement.TargetGroupId == group.TargetGroupId && entitlement.IsActive).ToArrayAsync(cancellationToken);
            foreach (var entitlement in entitlements) { entitlement.IsActive = false; entitlement.ModifiedUtc = now; entitlement.ModifiedBy = auditContext.FrontendClientId; }
            dbContext.AuditEvents.Add(CreateAuditEvent("DirectoryReconciliationTargetGroupDeactivated", finding, request.Actor.ObjectGuid, auditContext, now, null, group.DomainQualifiedName));
        }

        finding.IsResolved = true; finding.ResolvedUtc = now; finding.DeactivatedUtc = now; finding.DeactivatedByObjectGuid = request.Actor.ObjectGuid;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AdministrationUpdateResult.Updated;
        }
        catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "DeactivateFromFindingAsync hit a concurrent update for FindingId {FindingId}; rolled back.", request.FindingId); await transaction.RollbackAsync(cancellationToken); return AdministrationUpdateResult.Conflict; }
    }

    private async Task SetFinalStatusAsync(Guid runId, DirectoryReconciliationRunStatus status, string? failureCategory, DateTimeOffset completedUtc, CancellationToken cancellationToken)
    {
        var run = await dbContext.DirectoryReconciliationRuns.SingleOrDefaultAsync(item => item.RunId == runId && item.Status == DirectoryReconciliationRunStatus.Executing, cancellationToken);
        if (run is null) return;
        run.Status = status; run.CompletedUtc = completedUtc; run.FailureCategory = failureCategory;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static AuditEventEntity CreateAuditEvent(string eventType, DirectoryReconciliationFindingEntity finding, Guid administratorObjectGuid, AdministrationAuditContext context, DateTimeOffset occurredUtc, string? targetAccountDisplayName, string? targetGroupDisplayName)
        => new()
        {
            EventId = Guid.NewGuid(), EventType = eventType, OccurredUtc = occurredUtc,
            FrontendClientId = context.FrontendClientId, SourceIpAddress = context.SourceIpAddress,
            ClientSourceIpAddress = context.ClientSourceIpAddress,
            SourceComponent = "Api", CorrelationId = context.CorrelationId, Result = "Succeeded",
            AuthenticationMethod = "Windows", TargetAccountId = finding.EntityType == DirectoryReconciliationEntityType.DirectoryAccount ? finding.EntityId : null,
            TargetGroupId = finding.EntityType == DirectoryReconciliationEntityType.TargetGroup ? finding.EntityId : null,
            TargetAccountDisplayNameSnapshot = targetAccountDisplayName, TargetGroupDisplayNameSnapshot = targetGroupDisplayName,
            PolicyRequirementsSummary = $"finding:{finding.FindingId:D};reason:{finding.Reason};objectGuid:{finding.ObjectGuid:D};administrator:{administratorObjectGuid:D}"
        };
}
