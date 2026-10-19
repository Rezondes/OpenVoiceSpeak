using Microsoft.AspNetCore.Http;

namespace OVS.Manager.Accounts;

/// <summary>Package M28 (AC3): while "2FA Pflicht" is on and the user has no TOTP, every page leads to its setup.</summary>
internal sealed class TwoFactorGate(RequestDelegate next)
{
    /// <summary>The page where TOTP is set up.</summary>
    public const string SetupPath = "/profile/two-factor";

    static readonly string[] OpenPaths = [SetupPath, "/logout", "/login", "/healthz", "/culture", "/theme", "/_framework", "/_blazor"];

    /// <summary>Runs the gate for one request.</summary>
    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (!context.User.HasClaim(claim => claim.Type == AccountClaims.MustSetUpTwoFactor)
            || OpenPaths.Any(open => path.StartsWithSegments(open, StringComparison.OrdinalIgnoreCase)) || Path.HasExtension(path.Value))
            return next(context);
        context.Response.Redirect(SetupPath);
        return Task.CompletedTask;
    }
}
