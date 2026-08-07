using System.Security.Claims;
using ADDS.PIM.Application.Security;
using Microsoft.AspNetCore.Authorization;

namespace ADDS.PIM.Web.Security;

public sealed class PimUserAccessRequirement : IAuthorizationRequirement;

public sealed class PimUserAccessAuthorizationHandler(IApplicationAccessAuthorizer accessAuthorizer) : AuthorizationHandler<PimUserAccessRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PimUserAccessRequirement requirement)
    {
        var sid = context.User.FindFirst(ClaimTypes.PrimarySid)?.Value;
        if (sid is not null && await accessAuthorizer.IsAuthorizedAsync(sid, ApplicationAccessLevel.User, CancellationToken.None))
            context.Succeed(requirement);
    }
}

public static class PimUserAccessPolicy
{
    public const string Name = "PimUserAccess";
}
