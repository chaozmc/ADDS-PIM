using ADDS.PIM.Domain.Security;

namespace ADDS.PIM.Application.Authorization;

/// <summary>
/// Evaluates the current SQL authorization state. AD object re-resolution,
/// request signatures, MFA, replay protection, and execution remain separate
/// mandatory controls and must be applied by the orchestration workflow.
/// </summary>
public sealed class AuthorizeMembershipRequestUseCase(
    IMembershipAuthorizationDataSource dataSource,
    TimeProvider timeProvider)
{
    public async Task<AuthorizedMembershipRequest?> ExecuteAsync(
        AuthorizeMembershipRequestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Actor.DirectoryScopeId == Guid.Empty || command.Actor.ObjectGuid == Guid.Empty
            || command.TargetAccountId == Guid.Empty || command.TargetGroupId == Guid.Empty
            || command.RequestedTtlSeconds <= 0)
        {
            throw new ArgumentException("The authorization command is incomplete.", nameof(command));
        }

        var now = timeProvider.GetUtcNow();
        var actorIdentities = await dataSource.ResolveActorAsync(command.Actor, cancellationToken);
        var validActors = actorIdentities.Where(x =>
            x.AccountIsActive && x.IsEnabledInDirectory && x.IsWithinAllowedScope
            && x.PersonIsActive && IsCurrent(x.PersonValidFromUtc, x.PersonValidUntilUtc, now)
            && x.LinkIsActive && x.MayAuthenticate && IsCurrent(x.LinkValidFromUtc, x.LinkValidUntilUtc, now))
            .ToArray();

        if (validActors.Length != 1)
        {
            return null;
        }

        var actor = validActors[0];
        var contexts = await dataSource.FindContextsAsync(
            actor.PersonId,
            actor.AccountId,
            command.TargetAccountId,
            command.TargetGroupId,
            cancellationToken);

        var authorizedContexts = contexts.Where(x => IsAuthorized(x, command.RequestedTtlSeconds, now)).ToArray();
        if (authorizedContexts.Length != 1)
        {
            return null;
        }

        var context = authorizedContexts[0];

        return new AuthorizedMembershipRequest(
            context.PersonId,
            context.ActorAccountId,
            context.TargetAccountId,
            context.TargetGroupId,
            context.EntitlementId,
            context.PersonDisplayName,
            context.ActorAccountDisplayName,
            context.TargetAccountDisplayName,
            context.TargetGroupDisplayName,
            context.DirectoryScopeId,
            context.TargetAccountObjectGuid,
            context.TargetGroupObjectGuid,
            CreatePolicyRequirementsSummary(context),
            context.EntitlementRequiresSecondFactor ?? context.PolicyRequiresSecondFactor,
            context.PolicyAllowedSecondFactorTypes,
            context.EntitlementRequiresApproval ?? context.PolicyRequiresApproval);
    }

    private static bool IsAuthorized(MembershipAuthorizationContext context, long requestedTtlSeconds, DateTimeOffset now)
    {
        if (!context.TargetAccountIsActive || !context.TargetAccountIsEnabledInDirectory || !context.TargetAccountIsWithinAllowedScope
            || !context.TargetLinkIsActive || !context.TargetMayReceivePrivileges || !IsCurrent(context.TargetLinkValidFromUtc, context.TargetLinkValidUntilUtc, now)
            || !context.TargetGroupIsEnabledForRequests || !context.TargetGroupIsWithinAllowedScope || !context.GroupPolicyIsActive
            || !context.EntitlementIsActive || !IsCurrent(context.EntitlementValidFromUtc, context.EntitlementValidUntilUtc, now)
            || (context.PolicyRequiresSecondFactor && !context.PolicyAllowedSecondFactorTypes.IsValidPolicyValue())
            || !ConstraintsNarrowPolicy(context))
        {
            return false;
        }

        if (context.PolicyMinimumTtlSeconds <= 0 || context.PolicyMaximumTtlSeconds < context.PolicyMinimumTtlSeconds
            || context.PolicyTtlStepSeconds <= 0)
        {
            return false;
        }

        var minimum = context.EntitlementMinimumTtlSeconds ?? context.PolicyMinimumTtlSeconds;
        var maximum = context.EntitlementMaximumTtlSeconds ?? context.PolicyMaximumTtlSeconds;
        var step = context.EntitlementTtlStepSeconds ?? context.PolicyTtlStepSeconds;
        return minimum > 0 && maximum >= minimum && step > 0
            && requestedTtlSeconds >= minimum && requestedTtlSeconds <= maximum && requestedTtlSeconds % step == 0;
    }

    private static bool ConstraintsNarrowPolicy(MembershipAuthorizationContext context)
        => (!context.EntitlementMinimumTtlSeconds.HasValue || context.EntitlementMinimumTtlSeconds >= context.PolicyMinimumTtlSeconds)
           && (!context.EntitlementMaximumTtlSeconds.HasValue || context.EntitlementMaximumTtlSeconds <= context.PolicyMaximumTtlSeconds)
           && (!context.EntitlementTtlStepSeconds.HasValue || context.EntitlementTtlStepSeconds >= context.PolicyTtlStepSeconds && context.EntitlementTtlStepSeconds % context.PolicyTtlStepSeconds == 0)
           && (!context.PolicyRequiresSecondFactor || context.EntitlementRequiresSecondFactor is not false)
           && (!context.PolicyRequiresTicket || context.EntitlementRequiresTicket is not false)
           && (!context.PolicyRequiresApproval || context.EntitlementRequiresApproval is not false);

    private static bool IsCurrent(DateTimeOffset validFromUtc, DateTimeOffset? validUntilUtc, DateTimeOffset now)
        => validFromUtc <= now && (validUntilUtc is null || now < validUntilUtc);

    private static string CreatePolicyRequirementsSummary(MembershipAuthorizationContext context)
        => $"ttl={context.PolicyMinimumTtlSeconds}-{context.PolicyMaximumTtlSeconds}s;step={context.PolicyTtlStepSeconds}s;secondFactor={(context.PolicyRequiresSecondFactor ? context.PolicyAllowedSecondFactorTypes : SecondFactorType.None)};ticket={context.PolicyRequiresTicket};approval={context.PolicyRequiresApproval}";
}
