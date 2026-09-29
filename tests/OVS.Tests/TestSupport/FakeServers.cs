using OVS.Client.Net;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;

namespace OVS.Tests.TestSupport;

/// <summary>A ServerViewModel without a network: own user "ich" is admin, "anna" sits in the linked channel.</summary>
public static class FakeServers
{
    public static readonly Guid Lobby = Guid.NewGuid(), Raid = Guid.NewGuid();

    public static ServerViewModel Admin(List<Request>? sent = null)
    {
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Gilde", "Hallo", true), Lobby,
            [new ChannelInfo(Lobby, "Lobby", "Start", 0), new ChannelInfo(Raid, "Raid", "", 1)],
            [new LinkInfo(Lobby, Raid)],
            [new GroupInfo(WellKnownGroups.Guest, "Gast", Permission.Speak), new GroupInfo(WellKnownGroups.Admin, "Admin", Permission.All)],
            [
                new UserInfo(1, "fp1", "ich", Lobby, false, false, false, Permission.All, [WellKnownGroups.Admin]),
                new UserInfo(2, "fp2", "anna", Raid, true, true, true, Permission.Speak, [WellKnownGroups.Guest]),
            ]);
        return new ServerViewModel(new StateMirror(new Welcome(1, "", snapshot)), r =>
        {
            sent?.Add(r);
            return Task.CompletedTask;
        }, TimeProvider.System);
    }

    /// <summary>Package 67: own user "ich" (admin) in the first channel, which is the default; all channels linked with each other.</summary>
    public static ServerViewModel WithChannels(params string[] names)
    {
        var channels = names.Select((n, i) => new ChannelInfo(Guid.NewGuid(), n, "", i)).ToList();
        var links = channels.SelectMany((a, i) => channels.Skip(i + 1).Select(b => new LinkInfo(a.Id, b.Id))).ToList();
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Gilde", "", false), channels[0].Id, channels, links,
            [new GroupInfo(WellKnownGroups.Admin, "Admin", Permission.All)],
            [new UserInfo(1, "fp1", "ich", channels[0].Id, false, false, false, Permission.All, [WellKnownGroups.Admin])]);
        return new ServerViewModel(new StateMirror(new Welcome(1, "", snapshot)), _ => Task.CompletedTask, TimeProvider.System);
    }
}
