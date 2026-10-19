using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using OVS.Manager.Accounts;
using OVS.Manager.Audit;
using OVS.Manager.Localization;

namespace OVS.Manager.Components.Pages;

/// <summary>Package M3 (AC1): the first account, made while none exists; it is the admin and is signed in at once.</summary>
sealed partial class Setup
{
    [Inject]
    FirstAdmin FirstAdmin { get; set; } = null!;

    [Inject]
    UserManager<ManagerUser> Users { get; set; } = null!;

    [Inject]
    SignInManager<ManagerUser> SignIn { get; set; } = null!;

    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    [Inject]
    AuditLog Audit { get; set; } = null!;

    [SupplyParameterFromForm]
    SetupInput? Model { get; set; }

    /// <summary>A fresh form on the first render; a post fills it.</summary>
    protected override void OnInitialized() => Model ??= new();

    /// <summary>Package M33 (AC3): why the last restore was refused, from the address.</summary>
    [SupplyParameterFromQuery(Name = "restore-error")]
    string? RestoreError { get; set; }

    /// <summary>Why the account was not made, null before a try.</summary>
    string? Error { get; set; }

    async Task CreateAsync()
    {
        if (Model!.Password != Model!.ConfirmPassword)
        {
            Error = Strings.Account_PasswordMismatch;
            return;
        }
        var result = await FirstAdmin.CreateAsync(Model!.UserName.Trim(), Model!.Password);
        if (!result.Succeeded)
        {
            Error = AccountErrors.Describe(result);
            return;
        }
        await Audit.WriteAsync(Model!.UserName.Trim(), AuditActions.AdminCreated, Model!.UserName.Trim()); // Package M5 (AC2)
        await SignIn.SignInAsync((await Users.FindByNameAsync(Model!.UserName.Trim()))!, isPersistent: false);
        Navigation.NavigateTo("/");
    }

    /// <summary>The fields of the form.</summary>
    sealed class SetupInput
    {
        public string UserName { get; set; } = "";
        public string Password { get; set; } = "";
        public string ConfirmPassword { get; set; } = "";
    }
}
