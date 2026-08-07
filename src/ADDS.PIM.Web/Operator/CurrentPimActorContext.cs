using System.Security.Claims;
using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Application.Security;

namespace ADDS.PIM.Web.Operator;

public interface ICurrentPimActorContext
{
    Task<AuthenticatedDirectoryAccount?> ResolveAsync(CancellationToken cancellationToken);
}

/// <summary>Derives the actor solely from the IIS-authenticated Windows SID.</summary>
public sealed class CurrentPimActorContext(IHttpContextAccessor httpContextAccessor, IApplicationAccessAuthorizer accessAuthorizer, DirectoryScopeConfiguration directoryScope) : ICurrentPimActorContext
{
    public async Task<AuthenticatedDirectoryAccount?> ResolveAsync(CancellationToken cancellationToken)
    {
        var sid = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.PrimarySid);
        var objectGuid = sid is null ? null : await accessAuthorizer.ResolveActorObjectGuidAsync(sid, cancellationToken);
        return objectGuid is Guid guid && guid != Guid.Empty ? new AuthenticatedDirectoryAccount(directoryScope.DirectoryScopeId, guid) : null;
    }
}
