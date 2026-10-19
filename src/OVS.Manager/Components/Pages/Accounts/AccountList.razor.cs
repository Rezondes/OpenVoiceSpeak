using Microsoft.AspNetCore.Components;
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
