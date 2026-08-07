namespace ADDS.PIM.Application.Security;

public enum ApplicationAccessLevel { User, Administrator }

public interface IApplicationAccessAuthorizer
{
    Task<bool> IsAuthorizedAsync(Guid actorObjectGuid, ApplicationAccessLevel requiredAccess, CancellationToken cancellationToken);
    Task<bool> IsAuthorizedAsync(string actorSid, ApplicationAccessLevel requiredAccess, CancellationToken cancellationToken);
    Task<Guid?> ResolveActorObjectGuidAsync(string actorSid, CancellationToken cancellationToken);
}
