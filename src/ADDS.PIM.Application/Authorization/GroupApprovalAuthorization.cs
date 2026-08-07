namespace ADDS.PIM.Application.Authorization;

/// <summary>
/// Answers "is this actor an approver, and for which groups" against current SQL
/// state. Distinct from <c>IApplicationAccessAuthorizer</c> (coarse, AD-group-based
/// User/Administrator access level): approver assignment is a fine-grained,
/// per-group, SQL-authoritative fact re-checked immediately before every decision.
/// </summary>
/// <summary>The approver's identity, resolved from their active MayApprove-capable authenticating account link.</summary>
public sealed record ApproverIdentity(Guid PersonId, Guid AccountId, string DisplayName);

public interface IGroupApprovalAuthorizer
{
    /// <summary>
    /// Resolves the actor to the person owning the active, MayApprove-capable
    /// authenticating account link, or null if the actor has none. This is the
    /// Approver right: it rides on the same account as MayAuthenticate.
    /// </summary>
    Task<ApproverIdentity?> ResolveApproverAsync(AuthenticatedDirectoryAccount actor, CancellationToken cancellationToken);

    /// <summary>Re-checked immediately before an approve/reject decision takes effect - never trusted from a prior list view.</summary>
    Task<bool> IsApproverForGroupAsync(Guid personId, Guid targetGroupId, CancellationToken cancellationToken);

    /// <summary>The set of target groups this person is currently an active approver for.</summary>
    Task<IReadOnlyList<Guid>> ListApprovableGroupIdsAsync(Guid personId, CancellationToken cancellationToken);
}
