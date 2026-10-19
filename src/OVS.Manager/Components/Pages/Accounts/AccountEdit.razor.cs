using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using OVS.Manager.Accounts;
using OVS.Manager.Localization;

namespace OVS.Manager.Components.Pages.Accounts;

/// <summary>Package M4 (AC2, AC3): the admin locks, unlocks, renames, resets and deletes a server manager.</summary>
sealed partial class AccountEdit
{
    [Inject]
    AccountService Accounts { get; set; } = null!;

    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    /// <summary>The account's id from the address.</summary>
    [Parameter]
    public string Id { get; set; } = "";

    [SupplyParameterFromForm(FormName = "rename")]
    RenameInput? RenameModel { get; set; }

    [SupplyParameterFromForm(FormName = "reset")]
    ResetInput? ResetModel { get; set; }

    /// <summary>The account, null when there is none with this id.</summary>
    AccountSummary? Account { get; set; }

    /// <summary>What went well, null before an action.</summary>
    string? Message { get; set; }

    /// <summary>What went wrong, null before an action.</summary>
    string? Error { get; set; }

    string Title => Account?.UserName ?? "";

    string LockText => Account?.IsLocked == true ? Strings.Account_Unlock : Strings.Account_Lock;

    /// <summary>Loads the account.</summary>
    protected override async Task OnInitializedAsync()
    {
        ResetModel ??= new();
        await LoadAsync();
        RenameModel ??= new() { UserName = Account?.UserName ?? "" };
        if (Account is null) Error = Strings.Account_NotFound;
    }

    async Task LoadAsync() => Account = (await Accounts.ListAsync()).FirstOrDefault(account => account.Id == Id);

    Task RenameAsync() => ShowAsync(Accounts.RenameAsync(Id, RenameModel!.UserName));

    Task ResetAsync() => ShowAsync(Accounts.ResetPasswordAsync(Id, ResetModel!.Password));

    Task ToggleLockAsync() => ShowAsync(Account?.IsLocked == true ? Accounts.UnlockAsync(Id) : Accounts.LockAsync(Id));

    async Task DeleteAsync()
    {
        var result = await Accounts.DeleteAsync(Id);
        if (result.Succeeded) Navigation.NavigateTo("/accounts");
        else Error = AccountErrors.Describe(result);
    }

    async Task ShowAsync(Task<IdentityResult> change)
    {
        var result = await change;
        if (result.Succeeded) Message = Strings.Account_Saved;
        else Error = AccountErrors.Describe(result);
        await LoadAsync();
    }

    /// <summary>The field of the rename form.</summary>
    sealed class RenameInput
    {
        public string UserName { get; set; } = "";
    }

    /// <summary>The field of the reset form.</summary>
    sealed class ResetInput
    {
        public string Password { get; set; } = "";
    }
}
