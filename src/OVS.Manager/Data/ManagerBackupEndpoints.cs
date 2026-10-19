using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OVS.Manager.Accounts;

namespace OVS.Manager.Data;

/// <summary>Package M33 (AC1, AC2): the download of the manager's backup and its restore from <c>/setup</c>, both forms with their antiforgery token.</summary>
internal static class ManagerBackupEndpoints
{
    /// <summary>The address of the restore; <see cref="SetupGate"/> treats it like the setup page.</summary>
    public const string RestorePath = "/setup/restore";

    /// <summary>Adds the endpoints to <paramref name="app"/>.</summary>
    public static void Map(WebApplication app)
    {
        app.MapPost("/settings/backup/download", async ([FromForm] string? password, [FromForm] string? confirm, HttpContext context, ManagerBackup backup, TimeProvider time) =>
        {
            if (ManagerBackup.PasswordProblem(password, confirm) is { } problem) return Results.LocalRedirect("/settings/backup?error=" + Uri.EscapeDataString(problem));
            var archive = await backup.DownloadAsync(password!, context.User.Identity?.Name ?? "", context.RequestAborted);
            return Results.File(archive, "application/octet-stream", $"ovs-manager-{time.GetUtcNow():yyyyMMdd-HHmm}{ManagerArchive.Extension}");
        }).RequireAuthorization(new AuthorizeAttribute { Roles = ManagerRoles.Admin });

        app.MapPost(RestorePath, async (IFormFile? file, [FromForm] string? password, ManagerRestore restore, HttpContext context) =>
        {
            if (file is null || password is null) return Results.LocalRedirect("/setup?restore-error=" + Uri.EscapeDataString(Localization.Strings.ManagerRestore_Unreadable));
            using var bytes = new MemoryStream();
            await file.CopyToAsync(bytes, context.RequestAborted);
            return await restore.RestoreAsync(bytes.ToArray(), password, context.RequestAborted) is { } refused
                ? Results.LocalRedirect("/setup?restore-error=" + Uri.EscapeDataString(refused))
                : Results.LocalRedirect("/login");
        }).AllowAnonymous();
    }
}
