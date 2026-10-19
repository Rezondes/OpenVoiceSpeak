using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OVS.Manager.Servers;

namespace OVS.Manager.Control;

/// <summary>Package M15 (AC1): <c>GET /servers/{id}/backups/download?file=…</c> streams a backup through the manager.</summary>
internal static class BackupEndpoints
{
    /// <summary>Adds the endpoint to <paramref name="app"/>, for the admin and the server's managers.</summary>
    public static void Map(WebApplication app) =>
        app.MapGet("/servers/{id}/backups/download", async (HttpContext context, string id, string file, BackupCommands backups) =>
        {
            if (file.Length == 0 || file.Contains('/', StringComparison.Ordinal) || file.Contains('\\', StringComparison.Ordinal)) return Results.BadRequest();
            context.Response.ContentType = "application/octet-stream";
            context.Response.Headers.ContentDisposition = $"attachment; filename=\"{file}\"";
            var failed = await backups.DownloadAsync(id, file, context.Response.Body, context.User.Identity?.Name ?? "", context.RequestAborted);
            return failed is null ? Results.Empty : Results.Problem(failed, statusCode: StatusCodes.Status502BadGateway);
        }).RequireAuthorization(ServerAccessHandler.Policy);
}
