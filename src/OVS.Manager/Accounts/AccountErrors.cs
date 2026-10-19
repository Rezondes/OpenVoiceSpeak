using Microsoft.AspNetCore.Identity;
using OVS.Manager.Localization;

namespace OVS.Manager.Accounts;

/// <summary>Package M3: the text of a refused account change in the page's language, from Identity's error codes.</summary>
internal static class AccountErrors
{
    /// <summary>The first error of <paramref name="result"/> as a user text.</summary>
    public static string Describe(IdentityResult result) => result.Errors.FirstOrDefault()?.Code switch
    {
        nameof(IdentityErrorDescriber.PasswordTooShort) => Strings.Account_PasswordTooShort,
        nameof(IdentityErrorDescriber.DuplicateUserName) or nameof(IdentityErrorDescriber.InvalidUserName) => Strings.Account_UserNameInvalid,
        nameof(IdentityErrorDescriber.PasswordMismatch) => Strings.Profile_OldPasswordWrong,
        AccountService.AdminProtected => Strings.Account_Protected,
        AccountService.NotFound => Strings.Account_NotFound,
        _ => Strings.Account_Failed,
    };
}
