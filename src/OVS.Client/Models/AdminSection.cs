namespace OVS.Client.Models;

/// <summary>
/// What the administration page asks of one of its sections (a tab or a card): ask the server for its lists, follow the
/// server's changed state, and let go of the server (its answer routes or subscriptions) when the page closes. A part a section has no use for is left out.
/// </summary>
internal sealed record AdminSection(Func<Task>? Refresh = null, Action? FollowServer = null, Action? Detach = null);
