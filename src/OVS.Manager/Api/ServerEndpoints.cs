using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Audit;
using OVS.Manager.Data;
using OVS.Manager.Exceptions;
using OVS.Manager.Localization;
using OVS.Manager.Servers;

namespace OVS.Manager.Api;

/// <summary>
/// Package M31 (AC2 to AC4): the REST API for scripts: list the servers the token's user sees, start, stop, restart and
/// update one of them. It reuses the lifecycle, the update and the access policy of M10; each call writes an audit line
/// naming the token; <c>/api/openapi.json</c> describes it.
/// </summary>
internal static class ServerEndpoints
{
    /// <summary>The policy of every call: a valid token.</summary>
    public const string Policy = "Api";

    /// <summary>The policy of the calls on one server: a valid token of a user who may operate it.</summary>
    public const string ServerPolicy = "ApiServer";

    /// <summary>Adds the two policies.</summary>
    public static void AddPolicies(AuthorizationOptions authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        authorization.AddPolicy(Policy, policy => policy.AddAuthenticationSchemes(ApiTokenHandler.SchemeName).RequireAuthenticatedUser());
        authorization.AddPolicy(ServerPolicy, policy => policy.AddAuthenticationSchemes(ApiTokenHandler.SchemeName).RequireAuthenticatedUser()
            .AddRequirements(new ServerAccessRequirement()));
    }

    /// <summary>Adds the endpoints to <paramref name="app"/>.</summary>
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/openapi.json", () => Results.Text(OpenApiDocument(), "application/json")).AllowAnonymous();
        var api = app.MapGroup("/api/servers").AddEndpointFilter(AuditAsync);
        api.MapGet("", ListAsync).RequireAuthorization(Policy);
        var server = api.MapGroup("/{id}").RequireAuthorization(ServerPolicy);
        server.MapPost("/start", (string id, HttpContext context, ServerLifecycle lifecycle) =>
            RunAsync(() => lifecycle.StartAsync(id, Actor(context), context.RequestAborted)));
        server.MapPost("/stop", (string id, HttpContext context, ServerLifecycle lifecycle) =>
            RunAsync(() => lifecycle.StopAsync(id, Actor(context), context.RequestAborted)));
        server.MapPost("/restart", (string id, HttpContext context, ServerLifecycle lifecycle) =>
            RunAsync(() => lifecycle.RestartAsync(id, Actor(context), context.RequestAborted)));
        server.MapPost("/update", UpdateAsync);
    }

    /// <summary>The servers the token's user sees, as the overview has them.</summary>
    static async Task<IResult> ListAsync(HttpContext context, ServerAccess access, StatusWatcher watcher)
    {
        var visible = await access.VisibleAsync(context.User, context.RequestAborted);
        return Results.Json(watcher.Servers.Where(server => visible is null || visible.Contains(server.Id))
            .Select(server => new { server.Id, server.Name, server.Port, server.Version, Status = server.Status.ToString() }));
    }

    /// <summary>A start, stop or restart: 204, or 409 while another action runs.</summary>
    static async Task<IResult> RunAsync(Func<Task<bool>> action)
    {
        try
        {
            return await action() ? Results.NoContent() : Results.Problem(Strings.Update_Busy, statusCode: StatusCodes.Status409Conflict);
        }
        catch (ServerRequestException exception)
        {
            return Results.Problem(ServerTexts.Problem(exception.Problem), statusCode: StatusCodes.Status404NotFound);
        }
        catch (PortInUseException)
        {
            return Results.Problem(ServerTexts.Problem(ServerProblem.PortInUseOnHost), statusCode: StatusCodes.Status409Conflict);
        }
        catch (DockerCallException)
        {
            return Results.Problem(Strings.Docker_CallFailed, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>Updates to <paramref name="version"/>, or to the newest offer without one: 204, or 409 with the reason.</summary>
    static async Task<IResult> UpdateAsync(string id, string? version, HttpContext context, ManagerDbContext database, ReleaseCatalog catalog, ServerUpdate update)
    {
        var server = await database.Servers.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id && row.DeletedAtUtc == null, context.RequestAborted);
        if (server is null) return Results.Problem(Strings.Servers_NotFound, statusCode: StatusCodes.Status404NotFound);
        var target = string.IsNullOrWhiteSpace(version) ? catalog.UpdateFor(server.Version)?.Version : version.Trim();
        if (target is null) return Results.Problem(Strings.Api_NoUpdate, statusCode: StatusCodes.Status409Conflict);
        return await update.UpdateAsync(id, target, Actor(context), cancellation: context.RequestAborted) is { } failed
            ? Results.Problem(failed, statusCode: StatusCodes.Status409Conflict)
            : Results.NoContent();
    }

    /// <summary>AC3: one line per call with the token's name, the server and the answer's status.</summary>
    static async ValueTask<object?> AuditAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var result = await next(invocation);
        var context = invocation.HttpContext;
        int status = result is IStatusCodeHttpResult { StatusCode: { } code } ? code : StatusCodes.Status200OK;
        await context.RequestServices.GetRequiredService<AuditLog>().WriteAsync(Actor(context), AuditActions.ApiCalled,
            context.User.FindFirst(ApiTokenHandler.TokenClaim)?.Value, context.GetRouteValue("id") as string,
            $"{context.Request.Method} {context.Request.Path} {status}", context.RequestAborted);
        return result;
    }

    static string Actor(HttpContext context) => context.User.Identity?.Name ?? "";

    static string OpenApiDocument()
    {
        using var stream = typeof(ServerEndpoints).Assembly.GetManifestResourceStream("OVS.Manager.Api.openapi.json")
                           ?? throw new InvalidOperationException("openapi.json is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
