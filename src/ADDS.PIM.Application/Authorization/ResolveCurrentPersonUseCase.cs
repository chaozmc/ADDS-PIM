namespace ADDS.PIM.Application.Authorization;

/// <summary>
/// Resolves the authenticated Actor account to exactly one currently valid Person, for workflows
/// (MFA enrollment/verification) that are person-scoped rather than tied to a specific target group.
/// </summary>
public sealed class ResolveCurrentPersonUseCase(IMembershipAuthorizationDataSource dataSource, TimeProvider timeProvider)
{
    public async Task<Guid?> ExecuteAsync(AuthenticatedDirectoryAccount actor, CancellationToken cancellationToken)
    {
        if (actor.DirectoryScopeId == Guid.Empty || actor.ObjectGuid == Guid.Empty)
        {
            throw new ArgumentException("The actor is incomplete.", nameof(actor));
        }

        var now = timeProvider.GetUtcNow();
        var validActors = (await dataSource.ResolveActorAsync(actor, cancellationToken)).Where(item =>
            item.AccountIsActive && item.IsEnabledInDirectory && item.IsWithinAllowedScope && item.PersonIsActive && IsCurrent(item.PersonValidFromUtc, item.PersonValidUntilUtc, now)
            && item.LinkIsActive && item.MayAuthenticate && IsCurrent(item.LinkValidFromUtc, item.LinkValidUntilUtc, now)).ToArray();

        return validActors.Length == 1 ? validActors[0].PersonId : null;
    }

    private static bool IsCurrent(DateTimeOffset from, DateTimeOffset? until, DateTimeOffset now) => from <= now && (until is null || now < until);
}
