using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace OVS.Manager.Accounts;

/// <summary>Package M3 (AC5): <c>POST /logout</c> ends the session (with the form's antiforgery token) and leads to the login.</summary>
internal static class AccountEndpoints
{
    /// <summary>Adds the endpoint to <paramref name="app"/>.</summary>
    public static void Map(WebApplication app) =>
        app.MapPost("/logout", async (SignInManager<ManagerUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.LocalRedirect("/login");
        }).WithMetadata(new RequireAntiforgeryTokenAttribute());
}
