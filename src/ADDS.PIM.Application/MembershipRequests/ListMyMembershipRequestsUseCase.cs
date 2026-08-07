using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Domain.MembershipRequests;

namespace ADDS.PIM.Application.MembershipRequests;

/// <summary>
/// Returns request snapshots owned by the person currently resolved from the
/// authenticated Actor account. This is a read model only and must not be used
/// as authorization evidence for a new membership request.
/// </summary>
public sealed class ListMyMembershipRequestsUseCase(
    IMembershipAuthorizationDataSource authorizationDataSource,
    IMyMembershipRequestHistoryStore historyStore,
    TimeProvider timeProvider)
{
    public async Task<MembershipRequestHistoryPage?> ExecuteAsync(
        ListMyMembershipRequestsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Actor.DirectoryScopeId == Guid.Empty || command.Actor.ObjectGuid == Guid.Empty
            || command.PageNumber < 1 || command.PageSize is not (10 or 20 or 30 or 40 or 50)
            || (command.Month.HasValue && command.Month is < 1 or > 12)
            || (command.Year.HasValue && command.Year < 1)
            || command.EntitlementId == Guid.Empty || command.TargetGroupId == Guid.Empty)
        {
            throw new ArgumentException("The history query is incomplete.", nameof(command));
        }

        var now = timeProvider.GetUtcNow();
        var validActors = (await authorizationDataSource.ResolveActorAsync(command.Actor, cancellationToken))
            .Where(actor => actor.AccountIsActive && actor.IsEnabledInDirectory && actor.IsWithinAllowedScope
                && actor.PersonIsActive && IsCurrent(actor.PersonValidFromUtc, actor.PersonValidUntilUtc, now)
                && actor.LinkIsActive && actor.MayAuthenticate && IsCurrent(actor.LinkValidFromUtc, actor.LinkValidUntilUtc, now))
            .ToArray();

        if (validActors.Length != 1)
        {
            return null;
        }

        return await historyStore.ListForPersonAsync(
            validActors[0].PersonId,
            new(command.PageNumber, command.PageSize, command.Year, command.Month, command.EntitlementId, command.TargetGroupId),
            cancellationToken);
    }

    private static bool IsCurrent(DateTimeOffset validFromUtc, DateTimeOffset? validUntilUtc, DateTimeOffset now)
        => validFromUtc <= now && (validUntilUtc is null || now < validUntilUtc);
}

public sealed record ListMyMembershipRequestsCommand(
    AuthenticatedDirectoryAccount Actor,
    int PageNumber,
    int PageSize,
    int? Year,
    int? Month,
    Guid? EntitlementId,
    Guid? TargetGroupId);

public sealed record MembershipRequestHistoryFilter(
    int PageNumber,
    int PageSize,
    int? Year,
    int? Month,
    Guid? EntitlementId,
    Guid? TargetGroupId);

public sealed record MembershipRequestHistoryPage(
    IReadOnlyList<MembershipRequestHistoryItem> Items,
    int TotalCount,
    IReadOnlyList<int> AvailableYears,
    IReadOnlyList<MembershipRequestHistoryFilterOption> Entitlements,
    IReadOnlyList<MembershipRequestHistoryFilterOption> TargetGroups);

public sealed record MembershipRequestHistoryFilterOption(Guid Id, string DisplayName);

public sealed record MembershipRequestHistoryItem(
    Guid RequestId,
    Guid EntitlementId,
    Guid TargetGroupId,
    string TargetAccountDisplayName,
    string TargetGroupDisplayName,
    long RequestedTtlSeconds,
    DateTimeOffset CreatedUtc,
    MembershipRequestStatus Status,
    string Reason,
    string? TicketReference);

public interface IMyMembershipRequestHistoryStore
{
    Task<MembershipRequestHistoryPage> ListForPersonAsync(
        Guid personId,
        MembershipRequestHistoryFilter filter,
        CancellationToken cancellationToken);
}
