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
        var a = harness.Admit("admin", admin.Fingerprint);
        var guest = harness.Admit("gast");
        StateHarness.Drain(guest);

        harness.Handle(a, new CreateChannel("Raid", "") { RequestId = "c" });

        Assert.Equal("Raid", Assert.Single(StateHarness.Drain(guest).OfType<ChannelAdded>()).Channel.Name);
    }

    [Fact]
    public async Task ListLogs_AnsweredFromThreadPool_WaitForAsyncReadsIt()
    {
        using var harness = new StateHarness(TestServer.Grant(admin, "Admin"));
        var a = harness.Admit("admin", admin.Fingerprint);

        harness.Handle(a, new ListLogs { RequestId = "l" });

        Assert.Equal("l", (await StateHarness.WaitForAsync<LogList>(a)).RequestId);
    }

    [Fact]
    public void ChangeStoredData_GroupTakenAway_SessionHearsItsNewRights()
    {
        using var harness = new StateHarness(TestServer.Grant(admin, "Admin"));
        var a = harness.Admit("admin", admin.Fingerprint);
        StateHarness.Drain(a);

        harness.ChangeStoredData(d => d.Users.Single(u => u.Fingerprint == admin.Fingerprint).GroupIds.Remove(WellKnownGroups.Admin));

        var own = StateHarness.Drain(a).OfType<UserUpdated>().Single(u => u.User.SessionId == a.Id).User;
        Assert.DoesNotContain(WellKnownGroups.Admin, own.GroupIds);
    }
}
