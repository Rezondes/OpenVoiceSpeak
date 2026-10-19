using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using OVS.Manager.Api;
using OVS.Manager.Localization;

namespace OVS.Manager.Components.Pages.Accounts;

/// <summary>Package M31 (AC1): the user's API tokens; a new one's secret is shown once, and a token can be revoked.</summary>
sealed partial class ApiTokenPage
{
    [Inject]
    ApiTokenStore Store { get; set; } = null!;

    [Inject]
    TimeProvider Time { get; set; } = null!;

    [CascadingParameter]
    HttpContext HttpContext { get; set; } = null!;

    [SupplyParameterFromForm(FormName = "create-token")]
    CreateInput? CreateModel { get; set; }

    [SupplyParameterFromForm(FormName = "revoke-token")]
    RevokeInput? RevokeModel { get; set; }

    IReadOnlyList<ApiToken> Tokens { get; set; } = [];

    /// <summary>The new token's secret, only in the answer that made it.</summary>
    string? Secret { get; set; }

    string? Error { get; set; }

    string UserId => HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("No user is signed in.");

    string UserName => HttpContext.User.Identity?.Name ?? "";

    /// <summary>Loads the tokens.</summary>
    protected override async Task OnInitializedAsync()
    {
        CreateModel ??= new();
        RevokeModel ??= new();
        Tokens = await Store.ListAsync(UserId);
    }

    async Task CreateAsync()
    {
        Secret = await Store.CreateAsync(UserId, UserName, CreateModel!.Name, CreateModel.Days);
        Error = Secret is null ? TextFormat.Of(Strings.ApiTokens_Invalid, ApiTokenStore.MaxNameLength, ApiTokenStore.MaxDays) : null;
        Tokens = await Store.ListAsync(UserId);
    }

    async Task RevokeAsync()
    {
        await Store.RevokeAsync(UserId, RevokeModel!.TokenId, UserName);
        Tokens = await Store.ListAsync(UserId);
    }

    string LocalTime(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Time.LocalTimeZone).ToString("g", CultureInfo.CurrentCulture);

    /// <summary>The fields of a new token.</summary>
    sealed class CreateInput
    {
        public string Name { get; set; } = "";

        public int Days { get; set; } = 90;
    }

    /// <summary>The token to revoke.</summary>
    sealed class RevokeInput
    {
        public string TokenId { get; set; } = "";
    }
}
