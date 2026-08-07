using ADDS.PIM.Domain.Security;

namespace ADDS.PIM.Application.Authorization;

/// <summary>
/// Server-trusted directory identity of the account that authenticated the
/// request. This is not an HTTP transport model.
/// </summary>
public sealed record AuthenticatedDirectoryAccount(Guid DirectoryScopeId, Guid ObjectGuid);

public sealed record AuthorizeMembershipRequestCommand(
    AuthenticatedDirectoryAccount Actor,
    Guid TargetAccountId,
    Guid TargetGroupId,
    long RequestedTtlSeconds);

public sealed record ResolvedActorIdentity(
    Guid AccountId,
    Guid PersonId,
    bool AccountIsActive,
    bool IsEnabledInDirectory,
    bool IsWithinAllowedScope,
    bool PersonIsActive,
    DateTimeOffset PersonValidFromUtc,
    DateTimeOffset? PersonValidUntilUtc,
    bool LinkIsActive,
    bool MayAuthenticate,
    DateTimeOffset LinkValidFromUtc,
    DateTimeOffset? LinkValidUntilUtc);

public sealed record MembershipAuthorizationContext(
    Guid PersonId,
    Guid ActorAccountId,
    Guid TargetAccountId,
    Guid TargetGroupId,
    Guid EntitlementId,
    string PersonDisplayName,
    string ActorAccountDisplayName,
    string TargetAccountDisplayName,
    string TargetGroupDisplayName,
    bool TargetAccountIsActive,
    bool TargetAccountIsEnabledInDirectory,
    bool TargetAccountIsWithinAllowedScope,
    bool TargetLinkIsActive,
    bool TargetMayReceivePrivileges,
    DateTimeOffset TargetLinkValidFromUtc,
    DateTimeOffset? TargetLinkValidUntilUtc,
    bool TargetGroupIsEnabledForRequests,
    bool TargetGroupIsWithinAllowedScope,
    bool GroupPolicyIsActive,
    long PolicyMinimumTtlSeconds,
    long PolicyMaximumTtlSeconds,
    long PolicyDefaultTtlSeconds,
    long PolicyTtlStepSeconds,
    bool PolicyRequiresSecondFactor,
    SecondFactorType PolicyAllowedSecondFactorTypes,
    bool PolicyRequiresTicket,
    bool PolicyRequiresApproval,
    bool EntitlementIsActive,
    DateTimeOffset EntitlementValidFromUtc,
    DateTimeOffset? EntitlementValidUntilUtc,
    long? EntitlementMinimumTtlSeconds,
    long? EntitlementMaximumTtlSeconds,
    long? EntitlementTtlStepSeconds,
    bool? EntitlementRequiresSecondFactor,
    bool? EntitlementRequiresTicket,
    bool? EntitlementRequiresApproval,
    Guid DirectoryScopeId = default,
    Guid TargetAccountObjectGuid = default,
    Guid TargetGroupObjectGuid = default);

public sealed record AuthorizedMembershipRequest(
    Guid PersonId,
    Guid ActorAccountId,
    Guid TargetAccountId,
    Guid TargetGroupId,
    Guid EntitlementId,
    string PersonDisplayName,
    string ActorAccountDisplayName,
    string TargetAccountDisplayName,
    string TargetGroupDisplayName,
    Guid DirectoryScopeId,
    Guid TargetAccountObjectGuid,
    Guid TargetGroupObjectGuid,
    string PolicyRequirementsSummary,
    bool RequiresSecondFactor,
    SecondFactorType AllowedSecondFactorTypes,
    bool RequiresApproval);

public interface IMembershipAuthorizationDataSource
{
    Task<IReadOnlyList<ResolvedActorIdentity>> ResolveActorAsync(
        AuthenticatedDirectoryAccount actor,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MembershipAuthorizationContext>> FindContextsAsync(
        Guid personId,
        Guid actorAccountId,
        Guid targetAccountId,
        Guid targetGroupId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MembershipAuthorizationContext>> FindContextsForPersonAsync(
        Guid personId,
        Guid actorAccountId,
        CancellationToken cancellationToken);
}
