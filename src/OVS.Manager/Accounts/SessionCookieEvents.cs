using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using OVS.Manager.Localization;

namespace OVS.Manager.Accounts;

/// <summary>
/// Package M4: the login cookie carries its <see cref="ManagerSession"/> and is valid only while that session exists, so
/// "log out everywhere", a lock and a reset end other browsers at their next request. At sign-in the cookie learns whether
/// the start password must be changed, and the account's language becomes the browser's (M2 AC1). A page the user may
/// not see answers 403 instead of leading to the login (AC5).
/// </summary>
internal sealed class SessionCookieEvents(SessionStore sessions, UserManager<ManagerUser> users, TwoFactorPolicy twoFactor) : CookieAuthenticationEvents
{
    /// <inheritdoc/>
    public override async Task SigningIn(CookieSigningInContext context)
    {
        var identity = (ClaimsIdentity)context.Principal!.Identity!;
        var userId = identity.FindFirst(ClaimTypes.NameIdentifier)!.Value;
        var http = context.HttpContext;
        // A refresh (after the own password changed) keeps the session the browser has.
        var current = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value == userId ? http.User.FindFirst(AccountClaims.Session)?.Value : null;
        var session = current is not null && await sessions.IsActiveAsync(current, http.RequestAborted)
            ? current
            : await sessions.StartAsync(userId, http.Request.Headers.UserAgent.ToString(), http.Connection.RemoteIpAddress?.ToString(), http.RequestAborted);
        identity.AddClaim(new Claim(AccountClaims.Session, session));
        var user = await users.FindByIdAsync(userId);
        if (user?.MustChangePassword == true) identity.AddClaim(new Claim(AccountClaims.MustChangePassword, "true"));
        if (user is { TwoFactorEnabled: false } && await twoFactor.IsRequiredAsync(http.RequestAborted)) identity.AddClaim(new Claim(AccountClaims.MustSetUpTwoFactor, "true")); // Package M28
        if (UiCulture.Find(user?.Language) is { } culture) UiCulture.Store(http, culture);
        await base.SigningIn(context);
    }

    /// <inheritdoc/>
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal is null) return;
        if (context.Principal.FindFirst(AccountClaims.Session)?.Value is { } session && await sessions.IsActiveAsync(session, context.HttpContext.RequestAborted)) return;
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }

    /// <inheritdoc/>
    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
