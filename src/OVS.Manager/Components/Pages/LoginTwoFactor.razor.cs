using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using OVS.Manager.Accounts;
using OVS.Manager.Audit;
using OVS.Manager.Localization;

namespace OVS.Manager.Components.Pages;

/// <summary>Package M28 (AC2): after the password, a TOTP code or a recovery code (each works once).</summary>
sealed partial class LoginTwoFactor
{
    [Inject]
    SignInManager<ManagerUser> SignIn { get; set; } = null!;

    [Inject]
    LoginLimits Limits { get; set; } = null!;

    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    [Inject]
    AuditLog Audit { get; set; } = null!;

    [CascadingParameter]
    HttpContext HttpContext { get; set; } = null!;

    [SupplyParameterFromQuery]
    string? ReturnUrl { get; set; }

    [SupplyParameterFromForm]
    CodeInput? Model { get; set; }

    string? Error { get; set; }

    /// <summary>A fresh form on the first render; a post fills it.</summary>
    protected override void OnInitialized() => Model ??= new();

    async Task LoginAsync()
    {
        var address = HttpContext.Connection.RemoteIpAddress;
        if (await SignIn.GetTwoFactorAuthenticationUserAsync() is not { } user)
        {
            Navigation.NavigateTo("/login");
            return;
        }
        if (Limits.IsBlocked(address))
        {
            Error = Strings.Login_TooMany;
            return;
        }
        var code = Model!.Code.Trim();
        var digits = string.Concat(code.Where(char.IsDigit));
        var result = digits.Length == code.Replace(" ", "", StringComparison.Ordinal).Length && digits.Length == 6
            ? await SignIn.TwoFactorAuthenticatorSignInAsync(digits, isPersistent: false, rememberClient: false)
            : await SignIn.TwoFactorRecoveryCodeSignInAsync(code);
        await Audit.WriteAsync(user.UserName ?? user.Id, result.Succeeded ? AuditActions.Login : AuditActions.LoginFailed, user.UserName, detail: address?.ToString());
        if (!result.Succeeded)
        {
            Limits.RecordFailure(address);
            Error = Strings.TwoFactor_WrongCode;
            return;
        }
        Navigation.NavigateTo(LocalUrls.IsLocal(ReturnUrl) ? ReturnUrl! : "/");
    }

    /// <summary>The field of the form.</summary>
    sealed class CodeInput
    {
        public string Code { get; set; } = "";
    }
}
