using OVS.Client.Dialogs;
using OVS.Client.Net;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Tests.TestSupport;

/// <summary>A ServerViewModel without a network: own user "ich" is admin, "anna" sits in the linked channel.</summary>
public static class FakeServers
{
    public static readonly Guid Lobby = Guid.NewGuid(), Raid = Guid.NewGuid();

    /// <summary>The two groups every server here has: guests may speak, the Admin group holds all rights.</summary>
    public static readonly GroupInfo GuestGroup = new(WellKnownGroups.Guest, "Gast", Permission.Speak, true),
        AdminGroup = new(WellKnownGroups.Admin, "Admin", Permission.All, true);

    /// <summary>A third group for the tests that list one more than the two every server has.</summary>
    public static readonly GroupInfo ModeratorGroup = new(Guid.NewGuid(), "Moderator", Permission.Speak | Permission.UserKick, true);

    /// <summary>The own user "ich", in the lobby.</summary>
    static UserInfo Me(Permission rights, IReadOnlyList<Guid> groups) => new(1, "fp1", "ich", Lobby, false, false, false, rights, groups);

    /// <summary>The own user "ich" keeps the name and channel and gets exactly these rights and groups (none of the old ones stay), as the server would report it.</summary>
    public static void SetOwnRightsAndGroups(ServerViewModel server, Permission rights, IReadOnlyList<Guid> groups) =>
        server.Apply(new UserUpdated(Me(rights, groups)));

    /// <param name="reply">The server's answer to a request, applied at once (before SendAsync returns, the hardest order).</param>
    public static ServerViewModel Admin(List<Request>? sent = null, TimeProvider? time = null, ServerDialogs? dialogs = null, Func<Request, Message?>? reply = null)
    {
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Gilde", "Hallo", true), Lobby,
            [new ChannelInfo(Lobby, "Lobby", "Start", 0), new ChannelInfo(Raid, "Raid", "", 1)],
            [new LinkInfo(Lobby, Raid)],
            [GuestGroup, AdminGroup],
            [
                Me(Permission.All, [WellKnownGroups.Admin]),
                // Package 92: others come without rights, with the server's judgement instead
                new UserInfo(2, "fp2", "anna", Raid, true, true, true, Permission.None, [WellKnownGroups.Guest], CanBeModeratedByMe: true),
            ]);
        ServerViewModel? server = null;
        server = new ServerViewModel(Snapshots.Mirror(snapshot), r =>
        {
            sent?.Add(r);
            if (reply?.Invoke(r) is { } answer) server!.Apply(answer);
            return Task.CompletedTask;
        }, time ?? TimeProvider.System, dialogs);
        return server;
    }

    /// <summary>Package 67: own user "ich" (admin) in the first channel, which is the default; all channels linked with each other.</summary>
    public static ServerViewModel WithChannels(params string[] names)
    {
        var channels = names.Select((n, i) => new ChannelInfo(Guid.NewGuid(), n, "", i)).ToList();
        var links = channels.SelectMany((a, i) => channels.Skip(i + 1).Select(b => new LinkInfo(a.Id, b.Id))).ToList();
        var snapshot = Snapshots.Snapshot("Gilde", "", channels[0].Id, channels,
            [AdminGroup],
            [new UserInfo(1, "fp1", "ich", channels[0].Id, false, false, false, Permission.All, [WellKnownGroups.Admin])], links: links);
        return new ServerViewModel(Snapshots.Mirror(snapshot), _ => Task.CompletedTask, TimeProvider.System);
    }

    /// <summary>Package 78: 8 channels (all linked), four groups and 20 users; own user "ich" is admin.</summary>
    public static ServerViewModel Crowded()
    {
        var channels = Enumerable.Range(1, 8).Select(i => new ChannelInfo(Guid.NewGuid(), i == 1 ? "Lobby" : $"Raidgruppe Nummer {i}", "", i)).ToList();
        var links = channels.SelectMany((a, i) => channels.Skip(i + 1).Select(b => new LinkInfo(a.Id, b.Id))).ToList();
        var groups = new List<GroupInfo>
        {
            GuestGroup,
            ModeratorGroup,
            new(Guid.NewGuid(), "Veteranen der alten Gilde", Permission.Speak, true),
            AdminGroup,
        };
        var users = Enumerable.Range(1, 20).Select(i => new UserInfo((uint)i, $"fp{i}", i == 1 ? "ich" : $"Mitspieler{i}",
            channels[i % 8].Id, false, false, false, i == 1 ? Permission.All : Permission.None, // Package 92: others without rights
            [i == 1 ? WellKnownGroups.Admin : WellKnownGroups.Guest], CanBeModeratedByMe: i != 1)).ToList();
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Gilde", "Hallo", true, null, new ServerLimits(50, 30, true, true, new TimeOnly(4, 0))),
            channels[0].Id, channels, links, groups, users);
        return new ServerViewModel(Snapshots.Mirror(snapshot), _ => Task.CompletedTask, TimeProvider.System);
    }

    /// <summary>
    /// Package 122: 40 channels (each linked with the next) and 400 users, ten per channel; own user "ich" is admin in the
    /// first channel, which is the default.
    /// </summary>
    public static ServerViewModel Large(TimeProvider? time = null) =>
        new(new StateMirror(LargeWelcome()), _ => Task.CompletedTask, time ?? TimeProvider.System);

    /// <summary>The welcome of <see cref="Large"/>, for two servers with the same channels (the mirror copies it).</summary>
    public static Welcome LargeWelcome()
    {
        var channels = Enumerable.Range(1, 40).Select(i => new ChannelInfo(Guid.NewGuid(), i == 1 ? "Lobby" : $"Raid {i}", "", i)).ToList();
        var links = channels.Skip(1).Select((b, i) => new LinkInfo(channels[i].Id, b.Id)).ToList();
        var groups = new List<GroupInfo>
        {
            GuestGroup,
            AdminGroup,
        };
        var users = Enumerable.Range(1, 400).Select(i => new UserInfo((uint)i, $"fp{i}", i == 1 ? "ich" : $"Spieler{i:000}",
            channels[(i - 1) % 40].Id, false, false, false, i == 1 ? Permission.All : Permission.None,
            [i == 1 ? WellKnownGroups.Admin : WellKnownGroups.Guest], CanBeModeratedByMe: i != 1)).ToList();
        var snapshot = Snapshots.Snapshot("Gilde", "", channels[0].Id, channels, groups, users, links: links);
        return Snapshots.Welcome(snapshot);
    }
}
