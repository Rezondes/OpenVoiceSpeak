using Microsoft.AspNetCore.Identity;

namespace OVS.Manager.Accounts;

/// <summary>Package M3 (D5): an account of the manager; it signs in with its user name, the e-mail is optional.</summary>
internal sealed class ManagerUser : IdentityUser
{
    /// <summary>Package M4 (AC1): a start or reset password the user must replace before any other page.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Package M4 (M2 AC1): the language the user picked, "de" or "en", null while the browser decides.</summary>
    public string? Language { get; set; }
}
