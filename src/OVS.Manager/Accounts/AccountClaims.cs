namespace OVS.Manager.Accounts;

/// <summary>Package M4: the manager's own claims in the login cookie.</summary>
internal static class AccountClaims
{
    /// <summary>The <see cref="ManagerSession"/> of the cookie.</summary>
    public const string Session = "ovs:session";

    /// <summary>Present while the user must change the start password.</summary>
    public const string MustChangePassword = "ovs:must-change-password";
}
