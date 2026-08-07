using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Domain.Security;

namespace ADDS.PIM.Application.MembershipRequests;

public sealed record AvailableMembershipEntitlement(
    Guid EntitlementId, Guid TargetAccountId, Guid TargetGroupId, string TargetAccountDisplayName, string TargetGroupDisplayName,
    long MinimumTtlSeconds, long MaximumTtlSeconds, long DefaultTtlSeconds, long TtlStepSeconds,
    bool RequiresSecondFactor, bool RequiresApproval, bool RequiresTicket, SecondFactorType AllowedSecondFactorTypes);

/// <summary>Returns display-only options for the current actor. Submission always re-authorizes.</summary>
public sealed class ListMyAvailableEntitlementsUseCase(IMembershipAuthorizationDataSource dataSource, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<AvailableMembershipEntitlement>?> ExecuteAsync(AuthenticatedDirectoryAccount actor, CancellationToken cancellationToken)
    {
        if (actor.DirectoryScopeId == Guid.Empty || actor.ObjectGuid == Guid.Empty) throw new ArgumentException("The actor is incomplete.", nameof(actor));
        var now = timeProvider.GetUtcNow();
        var actors = (await dataSource.ResolveActorAsync(actor, cancellationToken)).Where(item =>
            item.AccountIsActive && item.IsEnabledInDirectory && item.IsWithinAllowedScope && item.PersonIsActive && IsCurrent(item.PersonValidFromUtc, item.PersonValidUntilUtc, now)
            && item.LinkIsActive && item.MayAuthenticate && IsCurrent(item.LinkValidFromUtc, item.LinkValidUntilUtc, now)).ToArray();
        if (actors.Length != 1) return null;

        var contexts = await dataSource.FindContextsForPersonAsync(actors[0].PersonId, actors[0].AccountId, cancellationToken);
        return contexts.Where(context => IsVisible(context, now)).Select(context =>
        {
            var minimum = context.EntitlementMinimumTtlSeconds ?? context.PolicyMinimumTtlSeconds;
            var maximum = context.EntitlementMaximumTtlSeconds ?? context.PolicyMaximumTtlSeconds;
            var step = context.EntitlementTtlStepSeconds ?? context.PolicyTtlStepSeconds;
            var preferred = Math.Clamp(context.PolicyDefaultTtlSeconds, minimum, maximum);
            var defaultTtl = preferred - ((preferred - minimum) % step);
            return new AvailableMembershipEntitlement(context.EntitlementId, context.TargetAccountId, context.TargetGroupId, context.TargetAccountDisplayName, context.TargetGroupDisplayName, minimum, maximum, defaultTtl, step,
                context.EntitlementRequiresSecondFactor ?? context.PolicyRequiresSecondFactor,
                context.EntitlementRequiresApproval ?? context.PolicyRequiresApproval,
                context.EntitlementRequiresTicket ?? context.PolicyRequiresTicket,
                context.PolicyAllowedSecondFactorTypes);
        }).OrderBy(item => item.TargetGroupDisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.TargetAccountDisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsVisible(MembershipAuthorizationContext c, DateTimeOffset now)
    {
        var min = c.EntitlementMinimumTtlSeconds ?? c.PolicyMinimumTtlSeconds; var max = c.EntitlementMaximumTtlSeconds ?? c.PolicyMaximumTtlSeconds; var step = c.EntitlementTtlStepSeconds ?? c.PolicyTtlStepSeconds;
        return c.TargetAccountIsActive && c.TargetAccountIsEnabledInDirectory && c.TargetAccountIsWithinAllowedScope && c.TargetLinkIsActive && c.TargetMayReceivePrivileges && IsCurrent(c.TargetLinkValidFromUtc, c.TargetLinkValidUntilUtc, now)
            && c.TargetGroupIsEnabledForRequests && c.TargetGroupIsWithinAllowedScope && c.GroupPolicyIsActive && c.EntitlementIsActive && IsCurrent(c.EntitlementValidFromUtc, c.EntitlementValidUntilUtc, now)
            && min > 0 && max >= min && step > 0 && c.PolicyDefaultTtlSeconds >= c.PolicyMinimumTtlSeconds && c.PolicyDefaultTtlSeconds <= c.PolicyMaximumTtlSeconds;
    }

    private static bool IsCurrent(DateTimeOffset from, DateTimeOffset? until, DateTimeOffset now) => from <= now && (until is null || now < until);
}
