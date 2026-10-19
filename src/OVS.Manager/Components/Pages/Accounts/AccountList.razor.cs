using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using OVS.Manager.Accounts;
using OVS.Manager.Localization;

namespace OVS.Manager.Components.Pages.Accounts;

/// <summary>Package M4 (AC1, AC5): the admin's list of accounts and the form for a new server manager.</summary>
sealed partial class AccountList
{
    [Inject]
    AccountService Accounts { get; set; } = null!;

    [SupplyParameterFromForm(FormName = "create")]
    NewAccountInput? NewModel { get; set; }

    /// <summary>Every account.</summary>
    IReadOnlyList<AccountSummary> Rows { get; set; } = [];

    /// <summary>What went well, null before an action.</summary>
    string? Message { get; set; }

    /// <summary>What went wrong, null before an action.</summary>
    string? Error { get; set; }

    [Inject]
    TwoFactorPolicy TwoFactor { get; set; } = null!;

    [CascadingParameter]
    HttpContext HttpContext { get; set; } = null!;

    /// <summary>Package M28 (AC3): "2FA Pflicht" is on.</summary>
    bool TwoFactorRequired { get; set; }

    string PolicyText => TwoFactorRequired ? Strings.TwoFactor_PolicyOn : Strings.TwoFactor_PolicyOff;

    string PolicyButton => TwoFactorRequired ? Strings.TwoFactor_PolicyTurnOff : Strings.TwoFactor_PolicyTurnOn;

    async Task TogglePolicyAsync()
    {
        TwoFactorRequired = !await TwoFactor.IsRequiredAsync();
        await TwoFactor.SetRequiredAsync(TwoFactorRequired, HttpContext.User.Identity?.Name ?? "");
    }

    /// <summary>Loads the list.</summary>
    protected override async Task OnInitializedAsync()
    {
        NewModel ??= new();
        Rows = await Accounts.ListAsync();
    }

    async Task CreateAsync()
    {
        var result = await Accounts.CreateManagerAsync(NewModel!.UserName, NewModel.Password, NewModel.Email);
        if (!result.Succeeded)
        {
            Error = AccountErrors.Describe(result);
            return;
        }
        Message = Strings.Accounts_Created;
        NewModel = new();
        Rows = await Accounts.ListAsync();
    }

    static string EditLink(AccountSummary account) => $"/accounts/{Uri.EscapeDataString(account.Id)}";

    static string RoleText(AccountSummary account) => account.Role == ManagerRoles.Admin ? Strings.Accounts_RoleAdmin : Strings.Accounts_RoleManager;

    static string StateText(AccountSummary account) =>
        string.Join(", ", new[] { account.IsLocked ? Strings.Accounts_Locked : null, account.MustChangePassword ? Strings.Accounts_MustChange : null }.OfType<string>());

    /// <summary>The fields of a new server manager.</summary>
    sealed class NewAccountInput
    {
        public string UserName { get; set; } = "";
        public string Password { get; set; } = "";
        public string? Email { get; set; }
    }
}
