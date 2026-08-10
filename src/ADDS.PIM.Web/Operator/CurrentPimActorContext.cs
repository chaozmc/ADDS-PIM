using System.Security.Claims;
using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Application.Security;
using Microsoft.AspNetCore.Components.Authorization;

namespace ADDS.PIM.Web.Operator;

public interface ICurrentPimActorContext
{
    Task<AuthenticatedDirectoryAccount?> ResolveAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Derives the actor solely from the IIS-authenticated Windows SID.
/// Uses AuthenticationStateProvider rather than IHttpContextAccessor: the latter
/// is only valid during the initial HTTP request (static prerendering) and is
/// null once a Blazor Server component runs interactively over its SignalR
/// circuit, which has no HttpContext of its own.
/// </summary>
public sealed class CurrentPimActorContext(AuthenticationStateProvider authenticationStateProvider, IApplicationAccessAuthorizer accessAuthorizer, DirectoryScopeConfiguration directoryScope) : ICurrentPimActorContext
{
    public async Task<AuthenticatedDirectoryAccount?> ResolveAsync(CancellationToken cancellationToken)
    {
        var authenticationState = await authenticationStateProvider.GetAuthenticationStateAsync();
        var sid = authenticationState.User.FindFirstValue(ClaimTypes.PrimarySid);
        var objectGuid = sid is null ? null : await accessAuthorizer.ResolveActorObjectGuidAsync(sid, cancellationToken);
        return objectGuid is Guid guid && guid != Guid.Empty ? new AuthenticatedDirectoryAccount(directoryScope.DirectoryScopeId, guid) : null;
    }
}
