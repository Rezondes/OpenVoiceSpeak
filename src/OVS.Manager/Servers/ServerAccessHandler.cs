using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace OVS.Manager.Servers;

/// <summary>
/// Package M10 (AC2): the policy <see cref="Policy"/> of a page with a server id in its address; a manager whose server
/// it is not gets 403, also for a typed address.
/// </summary>
internal sealed class ServerAccessHandler(ServerAccess access) : AuthorizationHandler<ServerAccessRequirement>
{
    /// <summary>The policy's name.</summary>
    public const string Policy = "ServerAccess";

    /// <summary>The name of the route value with the server's id.</summary>
    public const string RouteKey = "Id";

    /// <inheritdoc/>
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ServerAccessRequirement requirement)
    {
        var serverId = context.Resource switch
        {
            HttpContext http => http.GetRouteValue(RouteKey) as string,
            Microsoft.AspNetCore.Components.RouteData route => route.RouteValues.TryGetValue(RouteKey, out var value) ? value as string : null,
            _ => null,
        };
        if (serverId is not null && await access.MayOperateAsync(context.User, serverId)) context.Succeed(requirement);
    }}
