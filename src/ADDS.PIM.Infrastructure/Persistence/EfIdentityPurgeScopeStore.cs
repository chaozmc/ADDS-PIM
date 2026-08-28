using ADDS.PIM.Application.Administration;
using ADDS.PIM.Application.Audit;
using AdministrationActor = ADDS.PIM.Contracts.Administration.V1.AdministrationActor;
using ADDS.PIM.Domain.MembershipRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfIdentityPurgeScopeStore(PimDbContext dbContext, TimeProvider timeProvider, ILogger<EfIdentityPurgeScopeStore> logger) : IIdentityPurgeScopeStore
{
    public async Task<IReadOnlyList<IdentityPurgeCandidate>> ListCandidatesAsync(Guid directoryScopeId, CancellationToken cancellationToken)
    {
        var persons = await dbContext.Persons.AsNoTracking().Where(entity => !entity.IsActive)
            .Select(entity => new IdentityPurgeCandidate(PurgeInitiatorType.Person, entity.PersonId, entity.DisplayName)).ToArrayAsync(cancellationToken);
        var accounts = await dbContext.DirectoryAccounts.AsNoTracking().Where(entity => entity.DirectoryScopeId == directoryScopeId && !entity.IsActive)
            .Select(entity => new IdentityPurgeCandidate(PurgeInitiatorType.DirectoryAccount, entity.AccountId, entity.DomainQualifiedName)).ToArrayAsync(cancellationToken);
        var groups = await dbContext.TargetGroups.AsNoTracking().Where(entity => entity.DirectoryScopeId == directoryScopeId && !entity.IsEnabledForRequests)
            .Select(entity => new IdentityPurgeCandidate(PurgeInitiatorType.TargetGroup, entity.TargetGroupId, entity.DomainQualifiedName)).ToArrayAsync(cancellationToken);
        return persons.Concat(accounts).Concat(groups).OrderBy(candidate => candidate.InitiatorType).ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public async Task<PurgeScopePreview?> GetPreviewAsync(PurgeInitiatorType initiatorType, Guid initiatorId, Guid directoryScopeId, CancellationToken cancellationToken)
    {
        var scope = initiatorType switch
        {
            PurgeInitiatorType.Person => await FromPersonAsync(initiatorId, directoryScopeId, cancellationToken),
            PurgeInitiatorType.DirectoryAccount => await FromAccountAsync(initiatorId, directoryScopeId, cancellationToken),
            PurgeInitiatorType.TargetGroup => await FromGroupAsync(initiatorId, directoryScopeId, cancellationToken),
            _ => null
        };
        return scope is null ? null : await CompleteAsync(scope, cancellationToken);
    }

    public async Task<AdministrationUpdateResult> ExecuteAsync(PurgeInitiatorType initiatorType, Guid initiatorId, AdministrationActor actor, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var scope = initiatorType switch
        {
            PurgeInitiatorType.Person => await FromPersonAsync(initiatorId, actor.DirectoryScopeId, cancellationToken),
            PurgeInitiatorType.DirectoryAccount => await FromAccountAsync(initiatorId, actor.DirectoryScopeId, cancellationToken),
            PurgeInitiatorType.TargetGroup => await FromGroupAsync(initiatorId, actor.DirectoryScopeId, cancellationToken),
            _ => null
        };
        if (scope is null) return AdministrationUpdateResult.NotFound;
        var preview = await CompleteAsync(scope, cancellationToken);
        if (!preview.IsEligible)
        {
            logger.LogWarning("ExecuteAsync conflict for {InitiatorType} InitiatorId {InitiatorId}: {BlockingReason}", initiatorType, initiatorId, preview.BlockingReason);
            return AdministrationUpdateResult.Conflict;
        }

        var links = await dbContext.PersonAccountLinks.Where(link => scope.PersonIds.Contains(link.PersonId) || scope.AccountIds.Contains(link.AccountId)).ToArrayAsync(cancellationToken);
        var accountIds = scope.AccountIds.Union(links.Select(link => link.AccountId)).Distinct().ToArray();
        var personIds = scope.PersonIds.Union(links.Select(link => link.PersonId)).Distinct().ToArray();
        var groupApprovers = await dbContext.GroupApprovers.Where(entity => personIds.Contains(entity.PersonId) || scope.GroupIds.Contains(entity.TargetGroupId)).ToArrayAsync(cancellationToken);
        var entitlements = await dbContext.DirectEntitlements.Where(entity => personIds.Contains(entity.PersonId) || accountIds.Contains(entity.TargetAccountId) || scope.GroupIds.Contains(entity.TargetGroupId)).ToArrayAsync(cancellationToken);
        var requests = await dbContext.MembershipRequests.Where(entity => personIds.Contains(entity.PersonId) || accountIds.Contains(entity.ActorAccountId) || accountIds.Contains(entity.TargetAccountId) || scope.GroupIds.Contains(entity.TargetGroupId)).ToArrayAsync(cancellationToken);
        var requestIds = requests.Select(request => request.RequestId).ToArray();
        var histories = await dbContext.MembershipRequestStatusHistory.Where(entity => requestIds.Contains(entity.RequestId)).ToArrayAsync(cancellationToken);
        var mfa = await dbContext.MfaTransactions.Where(entity => requestIds.Contains(entity.RequestId) || personIds.Contains(entity.PersonId) || accountIds.Contains(entity.ActorAccountId) || accountIds.Contains(entity.TargetAccountId) || scope.GroupIds.Contains(entity.TargetGroupId)).ToArrayAsync(cancellationToken);
        var mfaIds = mfa.Select(entity => entity.MfaTransactionId).ToArray();
        var factors = await dbContext.TotpFactors.Where(entity => personIds.Contains(entity.PersonId)).ToArrayAsync(cancellationToken);
        var factorIds = factors.Select(entity => entity.TotpFactorId).ToArray();
        var usedSteps = await dbContext.TotpUsedTimeSteps.Where(entity => factorIds.Contains(entity.TotpFactorId) || mfaIds.Contains(entity.MfaTransactionId)).ToArrayAsync(cancellationToken);
        var credentials = await dbContext.Fido2Credentials.Where(entity => personIds.Contains(entity.PersonId)).ToArrayAsync(cancellationToken);
        var challenges = await dbContext.Fido2Challenges.Where(entity => personIds.Contains(entity.PersonId)).ToArrayAsync(cancellationToken);
        var accounts = await dbContext.DirectoryAccounts.Where(entity => accountIds.Contains(entity.AccountId)).ToArrayAsync(cancellationToken);
        var persons = await dbContext.Persons.Where(entity => personIds.Contains(entity.PersonId)).ToArrayAsync(cancellationToken);
        var groups = await dbContext.TargetGroups.Where(entity => scope.GroupIds.Contains(entity.TargetGroupId)).ToArrayAsync(cancellationToken);
        var policyIds = groups.Select(entity => entity.GroupPolicyId).ToArray();
        var policies = await dbContext.GroupPolicies.Where(entity => policyIds.Contains(entity.GroupPolicyId)).ToArrayAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        dbContext.AuditEvents.Add(new Entities.AuditEventEntity
        {
            EventId = Guid.NewGuid(), EventType = "PimIdentityPurged", OccurredUtc = now,
            PersonId = scope.Type == PurgeInitiatorType.Person ? scope.Id : null,
            TargetAccountId = scope.Type == PurgeInitiatorType.DirectoryAccount ? scope.Id : null,
            TargetGroupId = scope.Type == PurgeInitiatorType.TargetGroup ? scope.Id : null,
            PersonDisplayNameSnapshot = scope.Type == PurgeInitiatorType.Person ? scope.DisplayName : null,
            TargetAccountDisplayNameSnapshot = scope.Type == PurgeInitiatorType.DirectoryAccount ? scope.DisplayName : null,
            TargetGroupDisplayNameSnapshot = scope.Type == PurgeInitiatorType.TargetGroup ? scope.DisplayName : null,
            FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress, ClientSourceIpAddress = auditContext.ClientSourceIpAddress, SourceComponent = "Api",
            CorrelationId = auditContext.CorrelationId, Result = "Succeeded", AuthenticationMethod = "Windows",
            PolicyRequirementsSummary = $"initiatorType={scope.Type};initiatorId={scope.Id:D};administrator={actor.ObjectGuid:D};{preview.CanonicalScopeSummary}"
        });
        dbContext.PurgeEventOutbox.Add(new Entities.PurgeEventOutboxEntity
        {
            OutboxId = Guid.NewGuid(), EventId = PurgeEventLogIds.IdentityPurged, EventType = "PimIdentityPurged", CorrelationId = auditContext.CorrelationId,
            Payload = $"initiatorType={scope.Type};initiatorId={scope.Id:D};administrator={actor.ObjectGuid:D};{preview.CanonicalScopeSummary}", CreatedUtc = now
        });

        dbContext.TotpUsedTimeSteps.RemoveRange(usedSteps);
        dbContext.MfaTransactions.RemoveRange(mfa);
        dbContext.MembershipRequestStatusHistory.RemoveRange(histories);
        dbContext.MembershipRequests.RemoveRange(requests);
        dbContext.DirectEntitlements.RemoveRange(entitlements);
        dbContext.TotpFactors.RemoveRange(factors);
        dbContext.Fido2Credentials.RemoveRange(credentials);
        dbContext.Fido2Challenges.RemoveRange(challenges);
        dbContext.PersonAccountLinks.RemoveRange(links);
        dbContext.GroupApprovers.RemoveRange(groupApprovers);
        dbContext.TargetGroups.RemoveRange(groups);
        dbContext.GroupPolicies.RemoveRange(policies);
        dbContext.DirectoryAccounts.RemoveRange(accounts);
        dbContext.Persons.RemoveRange(persons);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AdministrationUpdateResult.Updated;
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "ExecuteAsync failed to save the purge for {InitiatorType} InitiatorId {InitiatorId}; rolled back.", initiatorType, initiatorId);
            await transaction.RollbackAsync(cancellationToken);
            return AdministrationUpdateResult.Conflict;
        }
    }

    private async Task<Scope?> FromPersonAsync(Guid personId, Guid directoryScopeId, CancellationToken cancellationToken)
    {
        var person = await dbContext.Persons.AsNoTracking().SingleOrDefaultAsync(entity => entity.PersonId == personId, cancellationToken);
        if (person is null) return null;
        var accountIds = await (from link in dbContext.PersonAccountLinks.AsNoTracking()
                                join account in dbContext.DirectoryAccounts.AsNoTracking() on link.AccountId equals account.AccountId
                                where link.PersonId == personId && account.DirectoryScopeId == directoryScopeId
                                select link.AccountId).ToArrayAsync(cancellationToken);
        var hasOutsideScopeAccount = await (from link in dbContext.PersonAccountLinks.AsNoTracking()
                                            join account in dbContext.DirectoryAccounts.AsNoTracking() on link.AccountId equals account.AccountId
                                            where link.PersonId == personId && account.DirectoryScopeId != directoryScopeId
                                            select link.PersonAccountLinkId).AnyAsync(cancellationToken);
        return new(PurgeInitiatorType.Person, personId, person.DisplayName, [personId], accountIds, [], person.IsActive ? "The person is still active." : hasOutsideScopeAccount ? "The person has an account outside the authorized directory scope." : null);
    }

    private async Task<Scope?> FromAccountAsync(Guid accountId, Guid directoryScopeId, CancellationToken cancellationToken)
    {
        var account = await dbContext.DirectoryAccounts.AsNoTracking().SingleOrDefaultAsync(entity => entity.AccountId == accountId && entity.DirectoryScopeId == directoryScopeId, cancellationToken);
        if (account is null) return null;
        var links = await dbContext.PersonAccountLinks.AsNoTracking().Where(link => link.AccountId == accountId).ToArrayAsync(cancellationToken);
        var personIds = links.Select(link => link.PersonId).Distinct().ToArray();
        var otherAccounts = personIds.Length == 0 ? 0 : await dbContext.PersonAccountLinks.AsNoTracking().CountAsync(link => personIds.Contains(link.PersonId) && link.AccountId != accountId, cancellationToken);
        var blocking = account.IsActive ? "The directory account is still active." : links.Any(link => link.IsActive) ? "An account link is still active." : otherAccounts == 0 ? "The account is the person's last retained account; purge the person instead." : null;
        return new(PurgeInitiatorType.DirectoryAccount, accountId, account.DomainQualifiedName, personIds, [accountId], [], blocking);
    }

    private async Task<Scope?> FromGroupAsync(Guid groupId, Guid directoryScopeId, CancellationToken cancellationToken)
    {
        var group = await dbContext.TargetGroups.AsNoTracking().SingleOrDefaultAsync(entity => entity.TargetGroupId == groupId && entity.DirectoryScopeId == directoryScopeId, cancellationToken);
        if (group is null) return null;
        var policy = await dbContext.GroupPolicies.AsNoTracking().SingleOrDefaultAsync(entity => entity.GroupPolicyId == group.GroupPolicyId, cancellationToken);
        var blocking = group.IsEnabledForRequests ? "The target group is still enabled for requests." : policy?.IsActive == true ? "The target-group policy is still active." : null;
        return new(PurgeInitiatorType.TargetGroup, groupId, group.DomainQualifiedName, [], [], [groupId], blocking);
    }

    private async Task<PurgeScopePreview> CompleteAsync(Scope scope, CancellationToken cancellationToken)
    {
        var links = await dbContext.PersonAccountLinks.AsNoTracking().Where(link => scope.PersonIds.Contains(link.PersonId) || scope.AccountIds.Contains(link.AccountId)).ToArrayAsync(cancellationToken);
        var accountIds = scope.AccountIds.Union(links.Select(link => link.AccountId)).Distinct().ToArray();
        var personIds = scope.PersonIds.Union(links.Select(link => link.PersonId)).Distinct().ToArray();
        var groupApprovers = await dbContext.GroupApprovers.AsNoTracking().Where(entity => personIds.Contains(entity.PersonId) || scope.GroupIds.Contains(entity.TargetGroupId)).ToArrayAsync(cancellationToken);
        var entitlements = await dbContext.DirectEntitlements.AsNoTracking().Where(entity => personIds.Contains(entity.PersonId) || accountIds.Contains(entity.TargetAccountId) || scope.GroupIds.Contains(entity.TargetGroupId)).ToArrayAsync(cancellationToken);
        var requests = await dbContext.MembershipRequests.AsNoTracking().Where(entity => personIds.Contains(entity.PersonId) || accountIds.Contains(entity.ActorAccountId) || accountIds.Contains(entity.TargetAccountId) || scope.GroupIds.Contains(entity.TargetGroupId)).ToArrayAsync(cancellationToken);
        var requestIds = requests.Select(request => request.RequestId).ToArray();
        var histories = await dbContext.MembershipRequestStatusHistory.AsNoTracking().CountAsync(entity => requestIds.Contains(entity.RequestId), cancellationToken);
        var mfa = await dbContext.MfaTransactions.AsNoTracking().Where(entity => requestIds.Contains(entity.RequestId) || personIds.Contains(entity.PersonId) || accountIds.Contains(entity.ActorAccountId) || accountIds.Contains(entity.TargetAccountId) || scope.GroupIds.Contains(entity.TargetGroupId)).ToArrayAsync(cancellationToken);
        var mfaIds = mfa.Select(entity => entity.MfaTransactionId).ToArray();
        var factors = personIds.Length == 0 ? [] : await dbContext.TotpFactors.AsNoTracking().Where(entity => personIds.Contains(entity.PersonId)).ToArrayAsync(cancellationToken);
        var factorIds = factors.Select(entity => entity.TotpFactorId).ToArray();
        var usedSteps = factorIds.Length == 0 && mfaIds.Length == 0 ? 0 : await dbContext.TotpUsedTimeSteps.AsNoTracking().CountAsync(entity => factorIds.Contains(entity.TotpFactorId) || mfaIds.Contains(entity.MfaTransactionId), cancellationToken);
        var fidoCount = personIds.Length == 0 ? 0 : await dbContext.Fido2Credentials.AsNoTracking().CountAsync(entity => personIds.Contains(entity.PersonId), cancellationToken);
        var policies = scope.GroupIds.Length == 0 ? 0 : await dbContext.TargetGroups.AsNoTracking().Where(entity => scope.GroupIds.Contains(entity.TargetGroupId)).Select(entity => entity.GroupPolicyId).Distinct().CountAsync(cancellationToken);
        var blocking = scope.BlockingReason ?? (links.Any(link => link.IsActive) ? "A person-account link in the purge scope is still active." : groupApprovers.Any(entity => entity.IsActive) ? "A group-approver assignment in the purge scope is still active." : entitlements.Any(entity => entity.IsActive) ? "A direct entitlement in the purge scope is still active." : requests.Any(request => !IsTerminal(request.Status)) ? "A membership request in the purge scope is not terminal." : null);
        var summary = $"type={scope.Type};id={scope.Id:D};persons={personIds.Length};accounts={accountIds.Length};links={links.Length};groups={scope.GroupIds.Length};policies={policies};entitlements={entitlements.Length};requests={requests.Length};history={histories};mfa={mfa.Length};totpFactors={factors.Length};totpSteps={usedSteps};fido={fidoCount}";
        return new(scope.Type, scope.Id, scope.DisplayName, blocking is null, blocking, personIds.Length, accountIds.Length, links.Length, scope.GroupIds.Length, policies, entitlements.Length, requests.Length, histories, mfa.Length, factors.Length, usedSteps, fidoCount, summary);
    }

    private static bool IsTerminal(MembershipRequestStatus status) => status is MembershipRequestStatus.Succeeded or MembershipRequestStatus.Rejected or MembershipRequestStatus.Failed or MembershipRequestStatus.Expired or MembershipRequestStatus.Cancelled;
    private sealed record Scope(PurgeInitiatorType Type, Guid Id, string DisplayName, Guid[] PersonIds, Guid[] AccountIds, Guid[] GroupIds, string? BlockingReason);
}
