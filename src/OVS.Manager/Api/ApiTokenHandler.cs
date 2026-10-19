using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OVS.Manager.Accounts;

namespace OVS.Manager.Api;

/// <summary>
/// Package M31 (AC2): the scheme of the REST API. <c>Authorization: Bearer &lt;secret&gt;</c> signs in as the token's user
/// with that user's roles, while the token is valid and the account is not locked; the token's name rides along as a claim.
/// </summary>
internal sealed class ApiTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    ApiTokenStore tokens, UserManager<ManagerUser> users, SignInManager<ManagerUser> signIn)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The scheme's name.</summary>
    public const string SchemeName = "ApiToken";

    /// <summary>The claim with the token's name.</summary>
    public const string TokenClaim = "ovs:api-token";

    const string Bearer = "Bearer ";

    /// <inheritdoc/>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(Bearer, StringComparison.Ordinal)) return AuthenticateResult.NoResult();
        var token = await tokens.FindAsync(header[Bearer.Length..].Trim(), Context.RequestAborted);
        if (token is null) return AuthenticateResult.Fail("unknown or expired token");
        var user = await users.FindByIdAsync(token.UserId);
        if (user is null || await users.IsLockedOutAsync(user)) return AuthenticateResult.Fail("account locked");
        var claims = (await signIn.CreateUserPrincipalAsync(user)).Identities.First();
        var identity = new ClaimsIdentity(claims.Claims, SchemeName, claims.NameClaimType, claims.RoleClaimType);
        identity.AddClaim(new Claim(TokenClaim, token.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
