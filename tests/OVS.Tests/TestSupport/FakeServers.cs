using OVS.Client.Net;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;

namespace OVS.Tests.TestSupport;

/// <summary>A ServerViewModel without a network: own user "ich" is admin, "anna" sits in the linked channel.</summary>
public static class FakeServers
{
    public static readonly Guid Lobby = Guid.NewGuid(), Raid = Guid.NewGuid();

    public static ServerViewModel Admin(List<Request>? sent = null, TimeProvider? time = null, Dialogs? dialogs = null)
    {
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Gilde", "Hallo", true), Lobby,
            [new ChannelInfo(Lobby, "Lobby", "Start", 0), new ChannelInfo(Raid, "Raid", "", 1)],
            [new LinkInfo(Lobby, Raid)],
            [new GroupInfo(WellKnownGroups.Guest, "Gast", Permission.Speak, true), new GroupInfo(WellKnownGroups.Admin, "Admin", Permission.All, true)],
            [
                new UserInfo(1, "fp1", "ich", Lobby, false, false, false, Permission.All, [WellKnownGroups.Admin]),
                // Package 92: others come without rights, with the server's judgement instead
                new UserInfo(2, "fp2", "anna", Raid, true, true, true, Permission.None, [WellKnownGroups.Guest], CanBeModeratedByMe: true),
            ]);
        return new ServerViewModel(new StateMirror(new Welcome(1, "", snapshot)), r =>
        {
            sent?.Add(r);
            return Task.CompletedTask;
        }, time ?? TimeProvider.System, dialogs);
    }

    /// <summary>Package 67: own user "ich" (admin) in the first channel, which is the default; all channels linked with each other.</summary>
    public static ServerViewModel WithChannels(params string[] names)
    {
        var channels = names.Select((n, i) => new ChannelInfo(Guid.NewGuid(), n, "", i)).ToList();
        var links = channels.SelectMany((a, i) => channels.Skip(i + 1).Select(b => new LinkInfo(a.Id, b.Id))).ToList();
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Gilde", "", false), channels[0].Id, channels, links,
            [new GroupInfo(WellKnownGroups.Admin, "Admin", Permission.All, true)],
            [new UserInfo(1, "fp1", "ich", channels[0].Id, false, false, false, Permission.All, [WellKnownGroups.Admin])]);
        return new ServerViewModel(new StateMirror(new Welcome(1, "", snapshot)), _ => Task.CompletedTask, TimeProvider.System);
    }

    /// <summary>Package 78: 8 channels (all linked), four groups and 20 users; own user "ich" is admin.</summary>
    public static ServerViewModel Crowded()
    {
        var channels = Enumerable.Range(1, 8).Select(i => new ChannelInfo(Guid.NewGuid(), i == 1 ? "Lobby" : $"Raidgruppe Nummer {i}", "", i)).ToList();
        var links = channels.SelectMany((a, i) => channels.Skip(i + 1).Select(b => new LinkInfo(a.Id, b.Id))).ToList();
        var groups = new List<GroupInfo>
        {
            new(WellKnownGroups.Guest, "Gast", Permission.Speak, true),
            new(Guid.NewGuid(), "Moderator", Permission.Speak | Permission.UserKick, true),
            new(Guid.NewGuid(), "Veteranen der alten Gilde", Permission.Speak, true),
            new(WellKnownGroups.Admin, "Admin", Permission.All, true),
        };
        var users = Enumerable.Range(1, 20).Select(i => new UserInfo((uint)i, $"fp{i}", i == 1 ? "ich" : $"Mitspieler{i}",
            channels[i % 8].Id, false, false, false, i == 1 ? Permission.All : Permission.None, // Package 92: others without rights
            [i == 1 ? WellKnownGroups.Admin : WellKnownGroups.Guest], CanBeModeratedByMe: i != 1)).ToList();
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Gilde", "Hallo", true, null, new ServerLimits(50, 30, true, true, new TimeOnly(4, 0))),
            channels[0].Id, channels, links, groups, users);
        return new ServerViewModel(new StateMirror(new Welcome(1, "", snapshot)), _ => Task.CompletedTask, TimeProvider.System);
    }
}
