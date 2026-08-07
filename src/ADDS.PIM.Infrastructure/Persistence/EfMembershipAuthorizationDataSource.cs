using ADDS.PIM.Application.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

/// <summary>
/// Reads current authorization facts from SQL Server. It intentionally has no
/// authority to resolve or change Active Directory objects.
/// </summary>
public sealed class EfMembershipAuthorizationDataSource(PimDbContext dbContext) : IMembershipAuthorizationDataSource, ITicketReferencePolicySource
{
    public async Task<TicketReferencePolicy?> GetCurrentPolicyAsync(Guid targetGroupId, CancellationToken cancellationToken)
    {
        var policy = await (from targetGroup in dbContext.TargetGroups.AsNoTracking()
                            join item in dbContext.GroupPolicies.AsNoTracking() on targetGroup.GroupPolicyId equals item.GroupPolicyId
                            where targetGroup.TargetGroupId == targetGroupId
                            select new { item.GroupPolicyId, item.RequiresTicket }).SingleOrDefaultAsync(cancellationToken);
        if (policy is null) return null;
        var patterns = await dbContext.TicketReferencePatterns.AsNoTracking()
            .Where(item => item.GroupPolicyId == policy.GroupPolicyId && item.IsActive)
            .Select(item => new TicketReferencePattern(item.TicketReferencePatternId, item.Label, item.Expression))
            .ToListAsync(cancellationToken);
        return new TicketReferencePolicy(policy.RequiresTicket, patterns);
    }
    public async Task<IReadOnlyList<ResolvedActorIdentity>> ResolveActorAsync(
        AuthenticatedDirectoryAccount actor,
        CancellationToken cancellationToken)
        => await (
            from account in dbContext.DirectoryAccounts.AsNoTracking()
            join link in dbContext.PersonAccountLinks.AsNoTracking() on account.AccountId equals link.AccountId
            join person in dbContext.Persons.AsNoTracking() on link.PersonId equals person.PersonId
            where account.DirectoryScopeId == actor.DirectoryScopeId && account.ObjectGuid == actor.ObjectGuid
            select new ResolvedActorIdentity(
                account.AccountId,
                person.PersonId,
                account.IsActive,
                account.IsEnabledInDirectory,
                account.IsWithinAllowedScope,
                person.IsActive,
                person.ValidFromUtc,
                person.ValidUntilUtc,
                link.IsActive,
                link.MayAuthenticate,
                link.ValidFromUtc,
                link.ValidUntilUtc))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MembershipAuthorizationContext>> FindContextsAsync(
        Guid personId,
        Guid actorAccountId,
        Guid targetAccountId,
        Guid targetGroupId,
        CancellationToken cancellationToken)
        => await (
            from entitlement in dbContext.DirectEntitlements.AsNoTracking()
            join person in dbContext.Persons.AsNoTracking() on entitlement.PersonId equals person.PersonId
            join actorAccount in dbContext.DirectoryAccounts.AsNoTracking() on actorAccountId equals actorAccount.AccountId
            join targetAccount in dbContext.DirectoryAccounts.AsNoTracking() on entitlement.TargetAccountId equals targetAccount.AccountId
            join targetLink in dbContext.PersonAccountLinks.AsNoTracking() on targetAccount.AccountId equals targetLink.AccountId
            join targetGroup in dbContext.TargetGroups.AsNoTracking() on entitlement.TargetGroupId equals targetGroup.TargetGroupId
            join policy in dbContext.GroupPolicies.AsNoTracking() on targetGroup.GroupPolicyId equals policy.GroupPolicyId
            where entitlement.PersonId == personId
                && entitlement.TargetAccountId == targetAccountId
                && entitlement.TargetGroupId == targetGroupId
                && targetLink.PersonId == personId
            select new MembershipAuthorizationContext(
                person.PersonId,
                actorAccount.AccountId,
                targetAccount.AccountId,
                targetGroup.TargetGroupId,
                entitlement.EntitlementId,
                person.DisplayName,
                actorAccount.DomainQualifiedName,
                targetAccount.DomainQualifiedName,
                targetGroup.DomainQualifiedName,
                targetAccount.IsActive,
                targetAccount.IsEnabledInDirectory,
                targetAccount.IsWithinAllowedScope,
                targetLink.IsActive,
                targetLink.MayReceivePrivileges,
                targetLink.ValidFromUtc,
                targetLink.ValidUntilUtc,
                targetGroup.IsEnabledForRequests,
                targetGroup.IsWithinAllowedScope,
                policy.IsActive,
                policy.MinimumTtlSeconds,
                policy.MaximumTtlSeconds,
                policy.DefaultTtlSeconds,
                policy.AllowedTtlStepSeconds,
                policy.RequiresSecondFactor,
                policy.AllowedSecondFactorTypes,
                policy.RequiresTicket,
                policy.RequiresApproval,
                entitlement.IsActive,
                entitlement.ValidFromUtc,
                entitlement.ValidUntilUtc,
                entitlement.MinimumTtlSeconds,
                entitlement.MaximumTtlSeconds,
                entitlement.AllowedTtlStepSeconds,
                entitlement.RequiresSecondFactor,
                entitlement.RequiresTicket,
                entitlement.RequiresApproval,
                targetAccount.DirectoryScopeId,
                targetAccount.ObjectGuid,
                targetGroup.ObjectGuid))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MembershipAuthorizationContext>> FindContextsForPersonAsync(
        Guid personId,
        Guid actorAccountId,
        CancellationToken cancellationToken)
        => await (
            from entitlement in dbContext.DirectEntitlements.AsNoTracking()
            join person in dbContext.Persons.AsNoTracking() on entitlement.PersonId equals person.PersonId
            join actorAccount in dbContext.DirectoryAccounts.AsNoTracking() on actorAccountId equals actorAccount.AccountId
            join targetAccount in dbContext.DirectoryAccounts.AsNoTracking() on entitlement.TargetAccountId equals targetAccount.AccountId
            join targetLink in dbContext.PersonAccountLinks.AsNoTracking() on targetAccount.AccountId equals targetLink.AccountId
            join targetGroup in dbContext.TargetGroups.AsNoTracking() on entitlement.TargetGroupId equals targetGroup.TargetGroupId
            join policy in dbContext.GroupPolicies.AsNoTracking() on targetGroup.GroupPolicyId equals policy.GroupPolicyId
            where entitlement.PersonId == personId && targetLink.PersonId == personId
            select new MembershipAuthorizationContext(
                person.PersonId, actorAccount.AccountId, targetAccount.AccountId, targetGroup.TargetGroupId, entitlement.EntitlementId,
                person.DisplayName, actorAccount.DomainQualifiedName, targetAccount.DomainQualifiedName, targetGroup.DomainQualifiedName,
                targetAccount.IsActive, targetAccount.IsEnabledInDirectory, targetAccount.IsWithinAllowedScope, targetLink.IsActive, targetLink.MayReceivePrivileges, targetLink.ValidFromUtc, targetLink.ValidUntilUtc,
                targetGroup.IsEnabledForRequests, targetGroup.IsWithinAllowedScope, policy.IsActive, policy.MinimumTtlSeconds, policy.MaximumTtlSeconds, policy.DefaultTtlSeconds, policy.AllowedTtlStepSeconds,
                policy.RequiresSecondFactor, policy.AllowedSecondFactorTypes, policy.RequiresTicket, policy.RequiresApproval,
                entitlement.IsActive, entitlement.ValidFromUtc, entitlement.ValidUntilUtc, entitlement.MinimumTtlSeconds, entitlement.MaximumTtlSeconds, entitlement.AllowedTtlStepSeconds,
                entitlement.RequiresSecondFactor, entitlement.RequiresTicket, entitlement.RequiresApproval, targetAccount.DirectoryScopeId, targetAccount.ObjectGuid, targetGroup.ObjectGuid))
            .ToListAsync(cancellationToken);
}
