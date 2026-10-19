using Microsoft.AspNetCore.Authorization;

namespace OVS.Manager.Servers;

/// <summary>Package M10 (AC2): a page needs the access to the server in its address (<see cref="ServerAccessHandler"/>).</summary>
internal sealed class ServerAccessRequirement : IAuthorizationRequirement;
