using Microsoft.AspNetCore.Identity;

namespace OVS.Manager.Accounts;

/// <summary>Package M3 (D5): an account of the manager; it signs in with its user name, the e-mail is optional.</summary>
internal sealed class ManagerUser : IdentityUser;
