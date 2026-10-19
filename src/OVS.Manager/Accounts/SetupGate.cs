using Microsoft.AspNetCore.Http;

namespace OVS.Manager.Accounts;

/// <summary>
/// Package M3 (AC1): while no account exists every page leads to <c>/setup</c>; afterwards <c>/setup</c> answers 404.
/// The health check, the language and theme links and the files of the page stay reachable.
/// </summary>
internal sealed class SetupGate(RequestDelegate next, FirstAdmin firstAdmin)
{
    /// <summary>The setup page.</summary>
    public const string SetupPath = "/setup";

    static readonly string[] OpenPaths = ["/healthz", "/culture", "/theme", "/_framework"];

    /// <summary>Runs the gate for one request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        bool setup = path.Equals(SetupPath, StringComparison.OrdinalIgnoreCase);
        if (await firstAdmin.ExistsAsync(context.RequestAborted))
        {
            if (setup) context.Response.StatusCode = StatusCodes.Status404NotFound;
            else await next(context);
            return;
        }
        if (setup || IsOpen(path)) await next(context);
        else context.Response.Redirect(SetupPath);
    }

    /// <summary>The paths without an account: the health check, the switches, the framework and the static files.</summary>
    static bool IsOpen(PathString path) =>
        OpenPaths.Any(open => path.StartsWithSegments(open, StringComparison.OrdinalIgnoreCase))
        || Path.HasExtension(path.Value);
}
