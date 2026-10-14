using OVS.Client.Settings;
using OVS.Client.ViewModels;

namespace OVS.Client.Models;

/// <summary>Package 163: one hit of the quick switcher, a channel of the connected server or a saved server.</summary>
/// <param name="Title">The channel's or the server's name.</param>
/// <param name="Detail">The channel's user count, or "Server" (no address: streamer mode, Package 160).</param>
/// <param name="Channel">The channel Enter joins.</param>
/// <param name="Server">The saved server Enter connects to.</param>
internal sealed record QuickSwitchHit(string Title, string Detail, ChannelViewModel? Channel, Bookmark? Server);
