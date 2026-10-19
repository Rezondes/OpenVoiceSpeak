using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using OVS.Manager.Accounts;
using OVS.Manager.Audit;
using OVS.Manager.Localization;

namespace OVS.Manager.Components.Pages;

/// <summary>
/// Package M3 (AC3, AC5): sign-in by user name and password. One text for every failure (unknown user, wrong password,
/// locked account), so the page does not tell which accounts exist; an address with too many failures is refused first.
/// </summary>
sealed partial class Login
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
    LoginInput? Model { get; set; }

    /// <summary>A fresh form on the first render; a post fills it.</summary>
    protected override void OnInitialized() => Model ??= new();

    /// <summary>Why the sign-in failed, null before a try.</summary>
    string? Error { get; set; }

    async Task LoginAsync()
    {
        var address = HttpContext.Connection.RemoteIpAddress;
        var userName = Model!.UserName.Trim();
        if (Limits.IsBlocked(address))
        {
            await Audit.WriteAsync(userName, AuditActions.LoginRefused, userName, detail: address?.ToString()); // Package M5 (AC2)
            Error = Strings.Login_TooMany;
            return;
        }
        var result = await SignIn.PasswordSignInAsync(userName, Model!.Password, isPersistent: false, lockoutOnFailure: true);
        await Audit.WriteAsync(userName, result.Succeeded ? AuditActions.Login : AuditActions.LoginFailed, userName, detail: address?.ToString());
        if (!result.Succeeded)
        {
            Limits.RecordFailure(address);
            Error = Strings.Login_Wrong;
            return;
        }
        Navigation.NavigateTo(LocalUrls.IsLocal(ReturnUrl) ? ReturnUrl! : "/");
    }

    /// <summary>The fields of the form.</summary>
    sealed class LoginInput
    {
        public string UserName { get; set; } = "";
        public string Password { get; set; } = "";
    }
}
