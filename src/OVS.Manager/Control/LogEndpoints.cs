using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OVS.Manager.Servers;

namespace OVS.Manager.Control;

/// <summary>Package M16 (AC2): <c>GET /servers/{id}/log/download?file=server/…</c> streams a server log file through the manager.</summary>
internal static class LogEndpoints
{
    /// <summary>Adds the endpoint to <paramref name="app"/>, for the admin and the server's managers.</summary>
    public static void Map(WebApplication app) =>
        app.MapGet("/servers/{id}/log/download", async (HttpContext context, string id, string file, LogCommands logs) =>
        {
            if (!LogCommands.IsServerLog(file)) return Results.BadRequest();
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Headers.ContentDisposition = $"attachment; filename=\"{LogCommands.FileName(file)}\"";
            var failed = await logs.DownloadAsync(id, file, context.Response.Body, context.User.Identity?.Name ?? "", context.RequestAborted);
            return failed is null ? Results.Empty : Results.Problem(failed, statusCode: StatusCodes.Status502BadGateway);
        }).RequireAuthorization(ServerAccessHandler.Policy);
}
