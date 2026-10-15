using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.State;

/// <summary>Package 168: profile pictures per server, set by their owner, checked, handed out by hash.</summary>
public sealed class AvatarCommandTests
{
    static readonly byte[] Picture = TestPng.Rgba();

    [Fact]
    public async Task Set_OwnPicture_BroadcastsHashAndServes()
    {
        await using var server = await TestServer.StartAsync();
        await using var anna = await TestClient.ConnectAsync(server, "anna");
        await using var bert = await TestClient.ConnectAsync(server, "bert");
        var hash = AvatarHash.Of(Picture);

        await anna.SendAsync(new SetAvatar(Convert.ToBase64String(Picture)));
        Assert.Equal(hash, (await bert.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == anna.Id && userUpdated.User.Avatar is not null)).User.Avatar);
        Assert.True(File.Exists(Path.Combine(server.DataDir, "avatars", anna.Identity.Fingerprint + ".png")));
        await bert.SendAsync(new GetAvatar(hash) { RequestId = "a" });
        var answer = await bert.WaitForAsync<Avatar>(avatar => avatar.RequestId == "a");
        Assert.Equal(Convert.ToBase64String(Picture), answer.PngBase64);
        await using (var carl = await TestClient.ConnectAsync(server, "carl"))
            Assert.Equal(hash, carl.Welcome.Snapshot.Users.Single(user => user.SessionId == anna.Id).Avatar); // a later client sees it at once

        // a picture that fails the check is refused and nothing is written (here one with a text chunk)
        var withText = TestPng.Rgba(extraChunks: [TestPng.Chunk("tEXt", "Comment\0hallo"u8.ToArray())]);
        await bert.SendAsync(new SetAvatar(Convert.ToBase64String(withText)) { RequestId = "t" });
        Assert.Equal(ReasonCodes.InvalidValue, (await bert.ErrorAsync("t")).Code);
        Assert.False(File.Exists(Path.Combine(server.DataDir, "avatars", bert.Identity.Fingerprint + ".png")));
    }

    [Fact]
    public async Task OtherUser_Remove_NeedsUserDelete()
    {
        using var adminId = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(adminId, "Admin"));
        await using var admin = await TestClient.ConnectAsync(server, "chef", adminId);
        await using var anna = await TestClient.ConnectAsync(server, "anna");
        await using var bert = await TestClient.ConnectAsync(server, "bert");
        await anna.SendAsync(new SetAvatar(Convert.ToBase64String(Picture)));
        await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == anna.Id && userUpdated.User.Avatar is not null);

        await bert.SendAsync(new SetAvatar(null, anna.Identity.Fingerprint) { RequestId = "b" }); // a guest may not
        Assert.Equal(ReasonCodes.PermissionDenied, (await bert.ErrorAsync("b")).Code);
        await admin.SendAsync(new SetAvatar(Convert.ToBase64String(Picture), anna.Identity.Fingerprint) { RequestId = "s" }); // nobody sets another's
        Assert.Equal(ReasonCodes.PermissionDenied, (await admin.ErrorAsync("s")).Code);
        await admin.SendAsync(new SetAvatar(null, "../../server-data") { RequestId = "p" }); // no path
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("p")).Code);

        await admin.SendAsync(new SetAvatar(null, anna.Identity.Fingerprint));
        Assert.Null((await anna.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == anna.Id && userUpdated.User.Avatar is null)).User.Avatar);
        Assert.False(File.Exists(Path.Combine(server.DataDir, "avatars", anna.Identity.Fingerprint + ".png")));
    }

    [Fact]
    public async Task Set_TwiceQuickly_Throttled()
    {
        await using var server = await TestServer.StartAsync();
        await using var anna = await TestClient.ConnectAsync(server, "anna");
        await anna.SendAsync(new SetAvatar(Convert.ToBase64String(Picture)) { RequestId = "1" });
        await anna.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.Avatar is not null);
        await anna.SendAsync(new SetAvatar(null) { RequestId = "2" });
        Assert.Equal(ReasonCodes.RateLimited, (await anna.ErrorAsync("2")).Code);
        Assert.True(File.Exists(Path.Combine(server.DataDir, "avatars", anna.Identity.Fingerprint + ".png")));
    }
}
