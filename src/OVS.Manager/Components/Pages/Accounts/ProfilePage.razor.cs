using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using OVS.Manager.Accounts;
using OVS.Manager.Localization;

namespace OVS.Manager.Components.Pages.Accounts;

/// <summary>Package M4 (AC4): every user's own account: password (with the old one), e-mail, language and sessions.</summary>
sealed partial class ProfilePage
{
    [Inject]
    UserManager<ManagerUser> Users { get; set; } = null!;

    [Inject]
    SignInManager<ManagerUser> SignIn { get; set; } = null!;

    [Inject]
    AccountService Accounts { get; set; } = null!;

    [Inject]
    SessionStore Sessions { get; set; } = null!;

    [Inject]
    TimeProvider Time { get; set; } = null!;

    [CascadingParameter]
    HttpContext HttpContext { get; set; } = null!;

    [SupplyParameterFromForm(FormName = "password")]
    PasswordInput? PasswordModel { get; set; }

    [SupplyParameterFromForm(FormName = "email")]
    EmailInput? EmailModel { get; set; }

    [SupplyParameterFromForm(FormName = "language")]
    LanguageInput? LanguageModel { get; set; }

    ManagerUser user = null!;

    /// <summary>The user's sessions, newest first.</summary>
    List<ManagerSession> SessionList { get; set; } = [];

    /// <summary>The session of this browser.</summary>
    string? CurrentSession => HttpContext.User.FindFirst(AccountClaims.Session)?.Value;

    /// <summary>The start password is still in use.</summary>
    bool MustChange => user.MustChangePassword;

    /// <summary>What went well, null before an action.</summary>
    string? Message { get; set; }

    /// <summary>What went wrong, null before an action.</summary>
    string? Error { get; set; }

    /// <summary>Loads the account; a form that was not posted shows the stored values.</summary>
    protected override async Task OnInitializedAsync()
    {
        PasswordModel ??= new();
        EmailModel ??= new() { Email = null };
        LanguageModel ??= new() { Language = null };
        user = (await Users.GetUserAsync(HttpContext.User))!;
        EmailModel.Email ??= user.Email ?? "";
        LanguageModel.Language ??= user.Language ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        SessionList = await Sessions.ListAsync(user.Id);
    }

    async Task ChangePasswordAsync()
    {
        var result = await Accounts.ChangeOwnPasswordAsync(user, PasswordModel!.OldPassword, PasswordModel.NewPassword);
        if (!result.Succeeded)
        {
            Error = AccountErrors.Describe(result);
            return;
        }
        await SignIn.RefreshSignInAsync(user); // the cookie forgets the start password; the session stays
        Message = Strings.Profile_PasswordChanged;
    }

    async Task SaveEmailAsync()
    {
        var result = await Accounts.SetOwnEmailAsync(user, EmailModel!.Email);
        if (result.Succeeded) Message = Strings.Profile_EmailSaved;
        else Error = AccountErrors.Describe(result);
    }

    async Task SaveLanguageAsync()
    {
        if (UiCulture.Find(LanguageModel!.Language) is not { } culture) return;
        var result = await Accounts.SetOwnLanguageAsync(user, culture.TwoLetterISOLanguageName);
        if (!result.Succeeded)
        {
            Error = AccountErrors.Describe(result);
            return;
        }
        UiCulture.Store(HttpContext, culture);
        Message = Strings.Profile_LanguageSaved;
    }

    async Task EndOthersAsync()
    {
        if (CurrentSession is not { } current) return;
        await Accounts.EndOtherSessionsAsync(user, current);
        SessionList = await Sessions.ListAsync(user.Id);
        Message = Strings.Profile_OthersEnded;
    }

    /// <summary>"since" and the local time the session signed in.</summary>
    string Since(ManagerSession session) =>
        TextFormat.Of(Strings.Profile_SessionSince, TimeZoneInfo.ConvertTime(session.CreatedAt, Time.LocalTimeZone).ToString("g", CultureInfo.CurrentCulture));

    /// <summary>The fields of the password form.</summary>
    sealed class PasswordInput
    {
        public string OldPassword { get; set; } = "";
        public string NewPassword { get; set; } = "";
    }

    /// <summary>The field of the e-mail form; null until the stored address is shown.</summary>
    sealed class EmailInput
    {
        public string? Email { get; set; } = "";
    }

    /// <summary>The field of the language form; null until the stored language is shown.</summary>
    sealed class LanguageInput
    {
        public string? Language { get; set; } = "";
    }
}
