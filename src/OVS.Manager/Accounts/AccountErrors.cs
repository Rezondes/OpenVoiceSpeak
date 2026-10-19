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
        _ => Strings.Account_Failed,
    };
}
