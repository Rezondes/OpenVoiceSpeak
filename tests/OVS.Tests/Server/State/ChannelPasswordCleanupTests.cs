using OVS.Server;
using OVS.Server.Data.Records;
using OVS.Server.Security;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.State;

/// <summary>The wrong channel passwords of a session are forgotten when the session ends, so nothing piles up.</summary>
public sealed class ChannelPasswordCleanupTests : IDisposable
{
    static readonly Guid Secret = Guid.NewGuid();

    readonly StateHarness harness = new(d => d.Channels.Add(
        new ChannelRecord { Id = Secret, Name = "Geheim", Order = 1, StoredPassword = StoredPassword.Create("richtig") }));

    public void Dispose() => harness.Dispose();

    int Tracked => harness.State.channelPasswordGuesses.SessionCount;

    Session AdmitWithAWrongPassword()
    {
        var guest = harness.Admit("gast");
        harness.Handle(guest, new JoinChannel(Secret, "falsch") { RequestId = "j" });
        Assert.Contains(StateHarness.Drain(guest), m => m is Error { Code: ReasonCodes.WrongChannelPassword });
        return guest;
    }

    [Fact]
    public void WrongChannelPassword_SessionRunning_Tracked()
    {
        AdmitWithAWrongPassword();
        Assert.Equal(1, Tracked);
    }

    [Fact]
    public void Remove_SessionWithAWrongPassword_NothingLeft()
    {
        var guest = AdmitWithAWrongPassword();
        harness.State.Remove(guest);
        Assert.Equal(0, Tracked);
    }

    [Fact]
    public void CloseAll_SessionWithAWrongPassword_NothingLeft()
    {
        AdmitWithAWrongPassword();
        harness.State.CloseAll(new Disconnected(ReasonCodes.ServerShutdown));
        Assert.Equal(0, Tracked);
    }
}
