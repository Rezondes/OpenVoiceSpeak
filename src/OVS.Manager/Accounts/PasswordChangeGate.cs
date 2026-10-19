using Microsoft.AspNetCore.Http;

namespace OVS.Manager.Accounts;

/// <summary>Package M4 (AC1): while the start password is not changed, every page leads to the profile.</summary>
internal sealed class PasswordChangeGate(RequestDelegate next)
{
    /// <summary>The page where the password is changed.</summary>
    public const string ProfilePath = "/profile";

    static readonly string[] OpenPaths = [ProfilePath, "/logout", "/login", "/healthz", "/culture", "/theme", "/_framework"];

    /// <summary>Runs the gate for one request.</summary>
    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (!context.User.HasClaim(claim => claim.Type == AccountClaims.MustChangePassword)
            || OpenPaths.Any(open => path.StartsWithSegments(open, StringComparison.OrdinalIgnoreCase)) || Path.HasExtension(path.Value))
            return next(context);
        context.Response.Redirect(ProfilePath);
        return Task.CompletedTask;
    }
}
