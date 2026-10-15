using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Services;

/// <summary>Package 168: the client asks for each picture once and draws only a checked one with the right hash.</summary>
public sealed class AvatarPicturesTests
{
    static readonly Guid Lobby = Guid.NewGuid();
    static readonly byte[] Picture = TestPng.Rgba();

    static UserInfo Anna(AvatarHash? avatar) => new(2, "fp2", "anna", Lobby, false, false, false, Permission.None, [], Avatar: avatar);

    [Fact]
    public void Hash_Shown_AskedOnceTakenWhenValid()
    {
        var sent = new List<Request>();
        var snapshot = Snapshots.Snapshot("Gilde", "", Lobby, [new ChannelInfo(Lobby, "Lobby", "", 0)], [], [Snapshots.User(1, "ich", Lobby, Permission.None), Anna(null)]);
        var server = new ServerViewModel(Snapshots.Mirror(snapshot), request =>
        {
            sent.Add(request);
            return Task.CompletedTask;
        }, TimeProvider.System);
        var hash = AvatarHash.Of(Picture);

        server.Apply(new UserUpdated(Anna(hash)));
        server.Apply(new UserUpdated(Anna(hash) with { SelfMuted = true })); // another update with the same picture
        Assert.Equal(hash, Assert.Single(sent.OfType<GetAvatar>()).Hash); // asked once
        var anna = server.Tree.FindUser(2)!;
        Assert.Null(anna.AvatarPng);

        server.Apply(new Avatar(null, hash, Convert.ToBase64String(TestPng.Rgba(trailing: [0])))); // fails the check
        server.Apply(new Avatar(null, hash, Convert.ToBase64String(TestPng.Rgba(pixels: new byte[TestPng.Raw(128, 4).Length])))); // valid, another hash
        Assert.Null(anna.AvatarPng);
        server.Apply(new Avatar(null, hash, Convert.ToBase64String(Picture)));
        Assert.Equal(Picture, anna.AvatarPng);

        server.Apply(new UserUpdated(Anna(null))); // removed: the letter again
        Assert.Null(anna.AvatarPng);
        Assert.False(anna.CanRemoveAvatar);
    }
}
