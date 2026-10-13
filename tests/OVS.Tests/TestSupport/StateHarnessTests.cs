using OVS.Shared.Identity;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.LogAccess;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Tests.TestSupport;

public sealed class StateHarnessTests
{
    readonly ClientIdentity admin = ClientIdentity.Create();

    [Fact]
    public void CreateChannel_WithoutSocket_BroadcastReachesOtherSession()
    {
        using var harness = new StateHarness(TestServer.Grant(admin, "Admin"));
        var session = harness.Admit("admin", admin.Fingerprint);
        var guest = harness.Admit("gast");
        StateHarness.Drain(guest);

        harness.Handle(session, new CreateChannel("Raid", "") { RequestId = "c" });

        Assert.Equal("Raid", Assert.Single(StateHarness.Drain(guest).OfType<ChannelAdded>()).Channel.Name);
    }

    [Fact]
    public async Task ListLogs_AnsweredFromThreadPool_WaitForAsyncReadsIt()
    {
        using var harness = new StateHarness(TestServer.Grant(admin, "Admin"));
        var session = harness.Admit("admin", admin.Fingerprint);

        harness.Handle(session, new ListLogs { RequestId = "l" });

        Assert.Equal("l", (await StateHarness.WaitForAsync<LogList>(session)).RequestId);
    }

    [Fact]
    public void ChangeStoredData_GroupTakenAway_SessionHearsItsNewRights()
    {
        using var harness = new StateHarness(TestServer.Grant(admin, "Admin"));
        var session = harness.Admit("admin", admin.Fingerprint);
        StateHarness.Drain(session);

        harness.ChangeStoredData(serverData => serverData.Users.Single(user => user.Fingerprint == admin.Fingerprint).GroupIds.Remove(WellKnownGroups.Admin));

        var own = StateHarness.Drain(session).OfType<UserUpdated>().Single(userUpdated => userUpdated.User.SessionId == session.Id).User;
        Assert.DoesNotContain(WellKnownGroups.Admin, own.GroupIds);
    }
}
