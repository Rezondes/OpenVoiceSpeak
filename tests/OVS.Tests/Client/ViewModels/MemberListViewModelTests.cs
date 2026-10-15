using OVS.Client.Localization;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.ViewModels;

/// <summary>Package 167: the member list beside the chat.</summary>
public sealed class MemberListViewModelTests
{
    static readonly Guid Lobby = Guid.NewGuid(), Mod = Guid.NewGuid(), NoOne = Guid.NewGuid();

    static UserInfo User(uint id, string nickname, params Guid[] groups) => new(id, "fp" + id, nickname, Lobby, false, false, false, Permission.None, groups);

    static string[] Shown(MemberListViewModel list) =>
        [.. list.Sections.Select(section => section.Heading + ": " + string.Join(", ", section.Members.Select(member => member.Nickname)))];

    [Fact]
    public void Grouped_ByHighestGroup_FollowsChanges()
    {
        var snapshot = Snapshots.Snapshot("Gilde", "", Lobby, [new ChannelInfo(Lobby, "Lobby", "", 0)],
            [new GroupInfo(WellKnownGroups.Admin, "Admin", Permission.All), new GroupInfo(Mod, "Moderator", Permission.Speak),
             new GroupInfo(NoOne, "Leer", Permission.Speak), new GroupInfo(WellKnownGroups.Guest, "Gast", Permission.Speak)],
            [User(1, "ich", WellKnownGroups.Guest, WellKnownGroups.Admin), User(2, "bert", WellKnownGroups.Guest), User(3, "anna", WellKnownGroups.Guest, Mod),
             User(4, "carl")]);
        var server = new ServerViewModel(Snapshots.Mirror(snapshot), _ => Task.CompletedTask, TimeProvider.System);
        var list = new MemberListViewModel(server);

        // each user under the highest group only, a group without members has no heading, "Online" for those without a group
        Assert.Equal(["ADMIN (1): ich", "MODERATOR (1): anna", "GAST (1): bert", $"{Strings.Members_Online.ToUpperInvariant()} (1): carl"], Shown(list));

        server.Apply(new UserUpdated(User(3, "anna", WellKnownGroups.Guest))); // anna loses the moderator group
        server.Apply(new UserJoined(User(5, "dora", Mod)));
        server.Apply(new UserLeft(4));
        Assert.Equal(["ADMIN (1): ich", "MODERATOR (1): dora", "GAST (2): anna, bert"], Shown(list));

        list.Detach();
        server.Apply(new UserLeft(5));
        Assert.Contains("MODERATOR (1): dora", Shown(list)); // another server shown: this list no longer follows
    }
}
